// live_analysis_mcp.cpp: MCP server that connects to RFA Live Analysis named pipe
// Compile with: cl /O2 /MT /EHsc live_analysis_mcp.cpp /Fe:live_analysis_mcp.exe

#define _CRT_SECURE_NO_WARNINGS
#include <windows.h>
#include <stdio.h>
#include <string>
#include <vector>
#include <map>
#include <sstream>
#include <iostream>
#include <thread>
#include <mutex>
#include <condition_variable>
#include <atomic>

// Pipe name
static const wchar_t* kPipeName = L"\\\\.\\pipe\\RFA_LiveAnalysis";

// Global pipe handle
static HANDLE g_pipe = INVALID_HANDLE_VALUE;
static std::mutex g_pipeMutex;
static std::atomic<bool> g_connected(false);

// Connect to the named pipe. The caller holds g_pipeMutex (a std::mutex can't be locked twice by one thread:
// SendCommand reconnecting through a locking version threw, and the server quit, whenever the game had been
// closed or wasn't up yet at the first request).
bool ConnectToPipeLocked() {
    if (g_pipe != INVALID_HANDLE_VALUE) {
        CloseHandle(g_pipe);
    }

    g_pipe = CreateFileW(
        kPipeName,
        GENERIC_READ | GENERIC_WRITE,
        0, NULL,
        OPEN_EXISTING,
        0, NULL
    );

    g_connected = (g_pipe != INVALID_HANDLE_VALUE);
    return g_connected;
}

bool ConnectToPipe() {
    std::lock_guard<std::mutex> lock(g_pipeMutex);
    return ConnectToPipeLocked();
}

// After a failed write or read the pipe's stream can't be trusted: drop it, so the next command connects again.
void DropPipeLocked() {
    g_connected = false;
    if (g_pipe != INVALID_HANDLE_VALUE) CloseHandle(g_pipe);
    g_pipe = INVALID_HANDLE_VALUE;
}

// Read exactly `size` bytes: the pipe is a byte stream, so a long answer can arrive in several pieces.
bool ReadAllLocked(void* into, DWORD size) {
    BYTE* at = static_cast<BYTE*>(into);
    while (size > 0) {
        DWORD read = 0;
        if (!ReadFile(g_pipe, at, size, &read, NULL) || read == 0) return false;
        at += read;
        size -= read;
    }
    return true;
}

// Send command and get response. The answer is returned as it came, NUL bytes included (read gives raw memory).
std::string SendCommand(const std::string& cmd) {
    std::lock_guard<std::mutex> lock(g_pipeMutex);

    if (!g_connected || g_pipe == INVALID_HANDLE_VALUE) {
        if (!ConnectToPipeLocked()) {
            return "ERROR: Cannot connect to pipe (is the game running with [Mcp] Enabled=1?)";
        }
    }

    // Send command
    DWORD written;
    std::string cmdWithNewline = cmd + "\n";
    if (!WriteFile(g_pipe, cmdWithNewline.c_str(), (DWORD)cmdWithNewline.size(), &written, NULL)) {
        DropPipeLocked();
        return "ERROR: Write failed (the game closed the pipe; the next command connects again)";
    }

    // Read response length (4 bytes)
    DWORD length = 0;
    if (!ReadAllLocked(&length, 4)) {
        DropPipeLocked();
        return "ERROR: Failed to read length";
    }
    if (length > 64u * 1024u * 1024u) {
        DropPipeLocked();
        return "ERROR: Answer too long; the connection was reset";
    }

    // Read response data
    std::string answer(length, '\0');
    if (length > 0 && !ReadAllLocked(&answer[0], length)) {
        DropPipeLocked();
        return "ERROR: Failed to read data";
    }
    return answer;
}

// A 32-bit address as the game's pipe reads it (std::hex): "0x" and hex digits.
std::string Hex(int value) {
    char text[16];
    snprintf(text, sizeof(text), "0x%X", static_cast<unsigned>(value));
    return text;
}

// Bytes as hex digits, two a byte, a space every 16 bytes.
std::string HexBytes(const std::string& bytes) {
    static const char digits[] = "0123456789ABCDEF";
    std::string out;
    out.reserve(bytes.size() * 3);
    for (size_t i = 0; i < bytes.size(); ++i) {
        if (i > 0 && i % 16 == 0) out += ' ';
        const unsigned char b = static_cast<unsigned char>(bytes[i]);
        out += digits[b >> 4];
        out += digits[b & 15];
    }
    return out;
}

// Escape JSON string. Control characters and bytes past 0x7F (the game's text isn't always UTF-8) are written as
// \u00XX, so the answer is always valid JSON.
std::string EscapeJson(const std::string& s) {
    std::ostringstream oss;
    for (char c : s) {
        const unsigned char b = static_cast<unsigned char>(c);
        switch (c) {
            case '"': oss << "\\\""; break;
            case '\\': oss << "\\\\"; break;
            case '\n': oss << "\\n"; break;
            case '\r': oss << "\\r"; break;
            case '\t': oss << "\\t"; break;
            default:
                if (b < 0x20 || b >= 0x7F) {
                    char code[8];
                    snprintf(code, sizeof(code), "\\u%04X", b);
                    oss << code;
                } else {
                    oss << c;
                }
                break;
        }
    }
    return oss.str();
}

// Simple JSON value extractor
std::string ExtractJsonString(const std::string& json, const std::string& key) {
    std::string searchKey = "\"" + key + "\"";
    size_t pos = json.find(searchKey);
    if (pos == std::string::npos) return "";
    size_t colon = json.find(':', pos);
    if (colon == std::string::npos) return "";
    size_t start = json.find_first_not_of(" \t\n\r", colon + 1);
    if (start == std::string::npos) return "";
    if (json[start] == '"') {
        size_t end = json.find('"', start + 1);
        if (end == std::string::npos) return "";
        return json.substr(start + 1, end - start - 1);
    } else {
        size_t end = json.find_first_of(",}", start);
        if (end == std::string::npos) return "";
        return json.substr(start, end - start);
    }
}

// A whole number (decimal, or 0x hex in a string). Addresses past 0x7FFFFFFF keep their bits (Hex() prints them
// unsigned); a value that isn't a number gives 0 instead of an exception that would end the server.
int ExtractJsonInt(const std::string& json, const std::string& key) {
    std::string val = ExtractJsonString(json, key);
    if (val.empty()) return 0;
    try {
        return static_cast<int>(static_cast<unsigned>(std::stoll(val, nullptr, 0)));
    } catch (...) {
        return 0;
    }
}

// Build JSON-RPC response
std::string MakeResponse(const std::string& id, const std::string& result, bool isError = false) {
    std::ostringstream oss;
    oss << "{\"jsonrpc\":\"2.0\",\"id\":";
    if (id.empty()) oss << "null";
    else oss << id;  // as the request gave it: a number, or a string with its quotes
    if (isError) {
        oss << ",\"error\":{\"code\":-32603,\"message\":" << result << "}}";
    } else {
        oss << ",\"result\":" << result << "}";
    }
    return oss.str();
}

std::string MakeTextResult(const std::string& text) {
    std::ostringstream oss;
    oss << "{\"content\":[{\"type\":\"text\",\"text\":\"" << EscapeJson(text) << "\"}]}";
    return oss.str();
}

// The request's id as written in it (a number, or a string with its quotes); empty if it has none.
std::string RequestId(const std::string& request) {
    std::string id;
    size_t idPos = request.find("\"id\"");
    if (idPos != std::string::npos) {
        size_t colon = request.find(':', idPos);
        size_t start = request.find_first_not_of(" \t\n\r", colon + 1);
        if (start != std::string::npos) {
            if (request[start] == '"') {
                size_t end = request.find('"', start + 1);
                if (end != std::string::npos) {
                    id = request.substr(start, end - start + 1);
                }
            } else {
                size_t end = request.find_first_of(",} \t\r\n", start);
                id = request.substr(start, end - start);
            }
        }
    }
    return id;
}

// Handle JSON-RPC request
std::string HandleRequest(const std::string& request) {
    // Extract method
    std::string method = ExtractJsonString(request, "method");
    if (method.empty()) {
        return MakeResponse(RequestId(request), "\"Missing method\"", true);
    }

    const std::string id = RequestId(request);

    // A notification (no id, e.g. notifications/initialized) gets no answer.
    if (id.empty() && method.rfind("notifications/", 0) == 0) {
        return "";
    }

    // Extract params
    std::string params;
    size_t paramsPos = request.find("\"params\"");
    if (paramsPos != std::string::npos) {
        size_t brace = request.find('{', paramsPos);
        if (brace != std::string::npos) {
            int depth = 1;
            size_t i = brace + 1;
            for (; i < request.size() && depth > 0; ++i) {
                if (request[i] == '{') depth++;
                else if (request[i] == '}') depth--;
            }
            params = request.substr(brace, i - brace);
        }
    }

    if (method == "initialize") {
        return MakeResponse(id,
            "{\"protocolVersion\":\"2024-11-05\",\"capabilities\":{\"tools\":{}},\"serverInfo\":{\"name\":\"rfa-live-analysis\",\"version\":\"1.0.0\"}}");
    }
    else if (method == "tools/list") {
        std::string tools =
            "["
            "{\"name\":\"read_memory\",\"description\":\"Read game memory at address\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"address\":{\"type\":\"integer\"},\"size\":{\"type\":\"integer\"}},\"required\":[\"address\",\"size\"]}},"
            "{\"name\":\"write_memory\",\"description\":\"Write hex bytes to game memory\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"address\":{\"type\":\"integer\"},\"data_hex\":{\"type\":\"string\"}},\"required\":[\"address\",\"data_hex\"]}},"
            "{\"name\":\"get_game_state\",\"description\":\"Get current game state (player pos, camera, level, etc.)\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}},"
            "{\"name\":\"list_modules\",\"description\":\"List all loaded modules with base addresses\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}},"
            "{\"name\":\"hook_function\",\"description\":\"Register a function hook\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"address\":{\"type\":\"integer\"},\"prologue_size\":{\"type\":\"integer\"},\"name\":{\"type\":\"string\"}},\"required\":[\"address\",\"prologue_size\",\"name\"]}},"
            "{\"name\":\"unhook_function\",\"description\":\"Remove a function hook\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"}},\"required\":[\"name\"]}},"
            "{\"name\":\"list_hooks\",\"description\":\"List active hooks\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}},"
            "{\"name\":\"call_function\",\"description\":\"Call a function at address (dangerous)\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"address\":{\"type\":\"integer\"}},\"required\":[\"address\"]}},"
            "{\"name\":\"exec_command\",\"description\":\"Execute a console command\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"command\":{\"type\":\"string\"}},\"required\":[\"command\"]}},"
            "{\"name\":\"scan_memory\",\"description\":\"Scan memory for byte pattern\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"start_address\":{\"type\":\"integer\"},\"size\":{\"type\":\"integer\"},\"pattern_hex\":{\"type\":\"string\"},\"mask\":{\"type\":\"string\"}},\"required\":[\"start_address\",\"size\",\"pattern_hex\"]}},"
            "{\"name\":\"enum_memory\",\"description\":\"Enumerate all memory regions\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}},"
            "{\"name\":\"call_function_real\",\"description\":\"Call a game function with arguments (cdecl/stdcall/thiscall)\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"address\":{\"type\":\"integer\"},\"convention\":{\"type\":\"string\",\"enum\":[\"cdecl\",\"stdcall\",\"thiscall\"]},\"args\":{\"type\":\"array\",\"items\":{\"type\":\"integer\"}}},\"required\":[\"address\",\"convention\"]}},"
            "{\"name\":\"get_callstack\",\"description\":\"Capture current call stack\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}},"
            "{\"name\":\"get_registers\",\"description\":\"Get current CPU registers\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}},"
            "{\"name\":\"enum_game_objects\",\"description\":\"Enumerate game objects (vehicles, items, weapons)\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}},"
            "{\"name\":\"read_pointer_chain\",\"description\":\"Follow pointer chain from base through offsets\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"base\":{\"type\":\"integer\"},\"offsets\":{\"type\":\"array\",\"items\":{\"type\":\"integer\"}}},\"required\":[\"base\",\"offsets\"]}},"
            "{\"name\":\"set_breakpoint\",\"description\":\"Set software breakpoint (INT3) at address\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"address\":{\"type\":\"integer\"}},\"required\":[\"address\"]}},"
            "{\"name\":\"clear_breakpoint\",\"description\":\"Clear software breakpoint at address\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"address\":{\"type\":\"integer\"}},\"required\":[\"address\"]}},"
            "{\"name\":\"disassemble\",\"description\":\"Disassemble instructions at address\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"address\":{\"type\":\"integer\"},\"count\":{\"type\":\"integer\"}},\"required\":[\"address\"]}},"
            "{\"name\":\"find_xrefs\",\"description\":\"Find cross-references to address\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"target\":{\"type\":\"integer\"},\"range_start\":{\"type\":\"integer\"},\"range_end\":{\"type\":\"integer\"}},\"required\":[\"target\"]}},"
            "{\"name\":\"list_exports\",\"description\":\"List exports of a loaded module\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"module_base\":{\"type\":\"integer\"}},\"required\":[\"module_base\"]}},"
            "{\"name\":\"list_threads\",\"description\":\"Enumerate all threads with register context\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}},"
            "{\"name\":\"watch_memory\",\"description\":\"Set hardware data breakpoint via guard page\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"address\":{\"type\":\"integer\"},\"size\":{\"type\":\"integer\"}},\"required\":[\"address\",\"size\"]}},"
            "{\"name\":\"teleport_player\",\"description\":\"Teleport player to position\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"x\":{\"type\":\"number\"},\"y\":{\"type\":\"number\"},\"z\":{\"type\":\"number\"},\"yaw\":{\"type\":\"number\"},\"pitch\":{\"type\":\"number\"}},\"required\":[\"x\",\"y\",\"z\"]}},"
            "{\"name\":\"set_camera\",\"description\":\"Set camera position/rotation/FOV\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"x\":{\"type\":\"number\"},\"y\":{\"type\":\"number\"},\"z\":{\"type\":\"number\"},\"yaw\":{\"type\":\"number\"},\"pitch\":{\"type\":\"number\"},\"fov\":{\"type\":\"number\"}},\"required\":[\"x\",\"y\",\"z\"]}},"
            "{\"name\":\"send_key\",\"description\":\"Simulate keyboard input\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"key\":{\"type\":\"string\"},\"action\":{\"type\":\"string\",\"enum\":[\"down\",\"up\",\"press\"]}},\"required\":[\"key\"]}},"
            "{\"name\":\"send_mouse\",\"description\":\"Simulate mouse input\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"dx\":{\"type\":\"integer\"},\"dy\":{\"type\":\"integer\"},\"buttons\":{\"type\":\"integer\"},\"wheel\":{\"type\":\"integer\"}},\"required\":[\"dx\",\"dy\"]}},"
            "{\"name\":\"set_time\",\"description\":\"Set time of day (0-24)\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"hours\":{\"type\":\"number\"}},\"required\":[\"hours\"]}},"
            "{\"name\":\"set_god_mode\",\"description\":\"Toggle god mode\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"enabled\":{\"type\":\"boolean\"}},\"required\":[\"enabled\"]}},"
            "{\"name\":\"set_noclip\",\"description\":\"Toggle collision/noclip\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"enabled\":{\"type\":\"boolean\"}},\"required\":[\"enabled\"]}},"
            "{\"name\":\"give_item\",\"description\":\"Give item/weapon/vehicle via console\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"type\":{\"type\":\"string\",\"enum\":[\"item\",\"weapon\",\"vehicle\",\"ammo\"]},\"name\":{\"type\":\"string\"},\"count\":{\"type\":\"integer\"}},\"required\":[\"type\",\"name\"]}},"
            "{\"name\":\"set_game_speed\",\"description\":\"Set game speed multiplier\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"multiplier\":{\"type\":\"number\"}},\"required\":[\"multiplier\"]}},"
            "{\"name\":\"freeze_game\",\"description\":\"Freeze/unfreeze game time\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"enabled\":{\"type\":\"boolean\"}},\"required\":[\"enabled\"]}},"
            "{\"name\":\"lua_state\",\"description\":\"Inspect Lua state (globals, stack, registry) - thread-safe via main thread\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"target\":{\"type\":\"string\",\"enum\":[\"globals\",\"stack\",\"registry\"]}},\"required\":[\"target\"]}},"
            "{\"name\":\"lua_run\",\"description\":\"Execute arbitrary Lua code and capture returns - thread-safe via main thread\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"code\":{\"type\":\"string\"}},\"required\":[\"code\"]}},"
            "{\"name\":\"lua_get\",\"description\":\"Get global Lua variable - thread-safe via main thread\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"}},\"required\":[\"name\"]}},"
            "{\"name\":\"lua_set\",\"description\":\"Set global Lua variable - thread-safe via main thread\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"},\"value\":{\"type\":\"string\"}},\"required\":[\"name\",\"value\"]}},"
            "{\"name\":\"lua_call\",\"description\":\"Call Lua function with arguments - thread-safe via main thread\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"function\":{\"type\":\"string\"},\"args\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}},\"required\":[\"function\"]}},"
            "{\"name\":\"lua_safe\",\"description\":\"Execute arbitrary Lua code via vint_lua on main thread (thread-safe)\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"code\":{\"type\":\"string\"}},\"required\":[\"code\"]}},"
            "{\"name\":\"trace_calls\",\"description\":\"Enable function call tracing at address\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"address\":{\"type\":\"integer\"},\"max_logs\":{\"type\":\"integer\"}},\"required\":[\"address\"]}},"
            "{\"name\":\"vtable\",\"description\":\"View vtable at address\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"address\":{\"type\":\"integer\"},\"depth\":{\"type\":\"integer\"}},\"required\":[\"address\"]}},"
            "{\"name\":\"find_vtables\",\"description\":\"Scan for vtables in memory range\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"start\":{\"type\":\"integer\"},\"end\":{\"type\":\"integer\"}},\"required\":[\"start\",\"end\"]}},"
            "{\"name\":\"strings_ref\",\"description\":\"Find all references to a string address\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"address\":{\"type\":\"integer\"}},\"required\":[\"address\"]}},"
            "{\"name\":\"alloc_track\",\"description\":\"Track memory allocations (malloc/free/HeapAlloc/HeapFree)\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"enabled\":{\"type\":\"boolean\"}},\"required\":[\"enabled\"]}},"
            "{\"name\":\"heap_walk\",\"description\":\"Walk process heap entries\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}},"
            "{\"name\":\"seh_chain\",\"description\":\"Show structured exception handler chain\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}},"
            "{\"name\":\"perf_sample\",\"description\":\"Statistical profiling by sampling instruction pointer\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"count\":{\"type\":\"integer\"},\"interval_ms\":{\"type\":\"integer\"}}}},"
            "{\"name\":\"mem_diff\",\"description\":\"Snapshot or diff memory region\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"address\":{\"type\":\"integer\"},\"size\":{\"type\":\"integer\"},\"snapshot_id\":{\"type\":\"integer\"}},\"required\":[\"address\",\"size\",\"snapshot_id\"]}},"
            "{\"name\":\"signature\",\"description\":\"Generate IDA-style byte signature\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"address\":{\"type\":\"integer\"},\"length\":{\"type\":\"integer\"}},\"required\":[\"address\",\"length\"]}}"
            "]";
        return MakeResponse(id, "{\"tools\":" + tools + "}");
    }
    else if (method == "tools/call") {
        std::string toolName = ExtractJsonString(params, "name");
        if (toolName.empty()) {
            return MakeResponse(id, "\"Missing tool name\"", true);
        }

        // Extract arguments object
        std::string args;
        size_t argsPos = params.find("\"arguments\"");
        if (argsPos != std::string::npos) {
            size_t brace = params.find('{', argsPos);
            if (brace != std::string::npos) {
                int depth = 1;
                size_t i = brace + 1;
                for (; i < params.size() && depth > 0; ++i) {
                    if (params[i] == '{') depth++;
                    else if (params[i] == '}') depth--;
                }
                args = params.substr(brace, i - brace);
            }
        }

        std::string resultText;

        if (toolName == "read_memory") {
            int address = ExtractJsonInt(args, "address");
            int size = ExtractJsonInt(args, "size");
            std::string response = SendCommand("read " + Hex(address) + " " + std::to_string(size));
            // The game answers with the raw bytes (nothing when the address isn't readable), or a line starting
            // "ERROR:" from this server; the bytes are given as hex.
            if (response.rfind("ERROR:", 0) == 0)
                resultText = "{\"address\":\"" + Hex(address) + "\",\"error\":\"" + EscapeJson(response) + "\"}";
            else
                resultText = "{\"address\":\"" + Hex(address) + "\",\"size\":" + std::to_string(response.size()) + ",\"hex\":\"" + HexBytes(response) +
                             "\"" + (response.empty() ? ",\"note\":\"not readable\"" : "") + "}";
        }
        else if (toolName == "write_memory") {
            int address = ExtractJsonInt(args, "address");
            std::string dataHex = ExtractJsonString(args, "data_hex");
            std::string response = SendCommand("write " + Hex(address) + " " + dataHex);
            bool success = (response.find("OK") != std::string::npos);
            resultText = "{\"success\":" + std::string(success ? "true" : "false") + ",\"response\":\"" + EscapeJson(response) + "\"}";
        }
        else if (toolName == "get_game_state") {
            std::string response = SendCommand("state");
            resultText = "{\"raw\":\"" + EscapeJson(response) + "\"}";
        }
        else if (toolName == "list_modules") {
            std::string response = SendCommand("modules");
            resultText = "{\"raw\":\"" + EscapeJson(response) + "\"}";
        }
        else if (toolName == "hook_function") {
            int address = ExtractJsonInt(args, "address");
            int prologue = ExtractJsonInt(args, "prologue_size");
            std::string name = ExtractJsonString(args, "name");
            std::string response = SendCommand("hook " + Hex(address) + " " + std::to_string(prologue) + " " + name);
            bool success = (response.find("OK") != std::string::npos);
            resultText = "{\"success\":" + std::string(success ? "true" : "false") + ",\"response\":\"" + EscapeJson(response) + "\"}";
        }
        else if (toolName == "unhook_function") {
            std::string name = ExtractJsonString(args, "name");
            std::string response = SendCommand("unhook " + name);
            bool success = (response.find("OK") != std::string::npos);
            resultText = "{\"success\":" + std::string(success ? "true" : "false") + ",\"response\":\"" + EscapeJson(response) + "\"}";
        }
        else if (toolName == "list_hooks") {
            std::string response = SendCommand("hooks");
            resultText = "{\"raw\":\"" + EscapeJson(response) + "\"}";
        }
        else if (toolName == "call_function") {
            int address = ExtractJsonInt(args, "address");
            std::string response = SendCommand("call " + Hex(address));
            resultText = "{\"response\":\"" + EscapeJson(response) + "\"}";
        }
        else if (toolName == "exec_command") {
            std::string command = ExtractJsonString(args, "command");
            std::string response = SendCommand("exec " + command);
            resultText = "{\"response\":\"" + EscapeJson(response) + "\"}";
        }
        else if (toolName == "scan_memory") {
            int startAddr = ExtractJsonInt(args, "start_address");
            int size = ExtractJsonInt(args, "size");
            std::string pattern = ExtractJsonString(args, "pattern_hex");
            std::string mask = ExtractJsonString(args, "mask");
            std::string scanCmd = "scan " + Hex(startAddr) + " " + std::to_string(size) + " " + pattern;
            if (!mask.empty()) scanCmd += " " + mask;
            std::string response = SendCommand(scanCmd);
            resultText = "{\"matches\":\"" + EscapeJson(response) + "\"}";
        }
        else if (toolName == "enum_memory") {
            std::string response = SendCommand("enum_mem");
            resultText = "{\"regions\":\"" + EscapeJson(response) + "\"}";
        }
        else if (toolName == "call_function_real") {
            int address = ExtractJsonInt(args, "address");
            std::string convention = ExtractJsonString(args, "convention");

            // Extract args array
            std::vector<int> argValues;
            size_t argsArrayPos = args.find("\"args\"");
            if (argsArrayPos != std::string::npos) {
                size_t bracket = args.find('[', argsArrayPos);
                if (bracket != std::string::npos) {
                    size_t endBracket = args.find(']', bracket);
                    std::string arrayContent = args.substr(bracket + 1, endBracket - bracket - 1);
                    size_t pos = 0;
                    while (pos < arrayContent.size()) {
                        size_t comma = arrayContent.find(',', pos);
                        std::string valStr = arrayContent.substr(pos, comma - pos);
                        valStr.erase(0, valStr.find_first_not_of(" \t\n\r"));
                        valStr.erase(valStr.find_last_not_of(" \t\n\r") + 1);
                        if (!valStr.empty()) {
                            argValues.push_back(std::stoi(valStr, nullptr, 0));
                        }
                        if (comma == std::string::npos) break;
                        pos = comma + 1;
                    }
                }
            }

            std::ostringstream cmd;
            cmd << "call_func 0x" << std::hex << address << std::dec << " " << convention;
            for (int v : argValues) {
                cmd << " 0x" << std::hex << v << std::dec;
            }
            std::string response = SendCommand(cmd.str());
            resultText = "{\"response\":\"" + EscapeJson(response) + "\"}";
        }
        else if (toolName == "get_callstack") {
            std::string response = SendCommand("callstack");
            resultText = "{\"raw\":\"" + EscapeJson(response) + "\"}";
        }
        else if (toolName == "get_registers") {
            std::string response = SendCommand("registers");
            resultText = "{\"raw\":\"" + EscapeJson(response) + "\"}";
        }
        else if (toolName == "enum_game_objects") {
            std::string response = SendCommand("enum_objects");
            resultText = "{\"raw\":\"" + EscapeJson(response) + "\"}";
        }
        else if (toolName == "read_pointer_chain") {
            int base = ExtractJsonInt(args, "base");

            // Extract offsets array
            std::vector<int> offsets;
            size_t offsetsArrayPos = args.find("\"offsets\"");
            if (offsetsArrayPos != std::string::npos) {
                size_t bracket = args.find('[', offsetsArrayPos);
                if (bracket != std::string::npos) {
                    size_t endBracket = args.find(']', bracket);
                    std::string arrayContent = args.substr(bracket + 1, endBracket - bracket - 1);
                    size_t pos = 0;
                    while (pos < arrayContent.size()) {
                        size_t comma = arrayContent.find(',', pos);
                        std::string valStr = arrayContent.substr(pos, comma - pos);
                        valStr.erase(0, valStr.find_first_not_of(" \t\n\r"));
                        valStr.erase(valStr.find_last_not_of(" \t\n\r") + 1);
                        if (!valStr.empty()) {
                            offsets.push_back(std::stoi(valStr, nullptr, 0));
                        }
                        if (comma == std::string::npos) break;
                        pos = comma + 1;
                    }
                }
            }

            std::ostringstream cmd;
            cmd << "read_chain 0x" << std::hex << base << std::dec;
            for (int v : offsets) {
                cmd << " 0x" << std::hex << v << std::dec;
            }
            std::string response = SendCommand(cmd.str());
            resultText = "{\"response\":\"" + EscapeJson(response) + "\"}";
        }
        else if (toolName == "set_breakpoint") {
            int address = ExtractJsonInt(args, "address");
            std::string response = SendCommand("breakpoint set " + Hex(address));
            resultText = "{\"response\":\"" + EscapeJson(response) + "\"}";
        }
        else if (toolName == "clear_breakpoint") {
            int address = ExtractJsonInt(args, "address");
            std::string response = SendCommand("breakpoint clear " + Hex(address));
            resultText = "{\"response\":\"" + EscapeJson(response) + "\"}";
        }
        else if (toolName == "disassemble") {
            int address = ExtractJsonInt(args, "address");
            int count = ExtractJsonInt(args, "count");
            if (count <= 0) count = 20;
            std::string response = SendCommand("disasm " + Hex(address) + " " + std::to_string(count));
            resultText = "{\"response\":\"" + EscapeJson(response) + "\"}";
        }
        else if (toolName == "find_xrefs") {
            int target = ExtractJsonInt(args, "target");
            int rangeStart = ExtractJsonInt(args, "range_start");
            int rangeEnd = ExtractJsonInt(args, "range_end");
            std::ostringstream cmd;
            cmd << "xrefs 0x" << std::hex << target << std::dec;
            if (rangeStart > 0) cmd << " 0x" << std::hex << rangeStart << std::dec;
            if (rangeEnd > 0) cmd << " 0x" << std::hex << rangeEnd << std::dec;
            std::string response = SendCommand(cmd.str());
            resultText = "{\"response\":\"" + EscapeJson(response) + "\"}";
        }
        else if (toolName == "list_exports") {
            int base = ExtractJsonInt(args, "module_base");
            std::string response = SendCommand("exports " + Hex(base));
            resultText = "{\"response\":\"" + EscapeJson(response) + "\"}";
        }
        else if (toolName == "list_threads") {
            std::string response = SendCommand("threads");
            resultText = "{\"response\":\"" + EscapeJson(response) + "\"}";
        }
        else if (toolName == "watch_memory") {
            int address = ExtractJsonInt(args, "address");
            int size = ExtractJsonInt(args, "size");
            std::string response = SendCommand("watch " + Hex(address) + " " + std::to_string(size) + " rw");
            resultText = "{\"response\":\"" + EscapeJson(response) + "\"}";
        }
        else if (toolName == "teleport_player") {
            double x = 0, y = 0, z = 0, yaw = 0, pitch = 0;
            std::string xStr = ExtractJsonString(args, "x");
            std::string yStr = ExtractJsonString(args, "y");
            std::string zStr = ExtractJsonString(args, "z");
            std::string yawStr = ExtractJsonString(args, "yaw");
            std::string pitchStr = ExtractJsonString(args, "pitch");
            if (!xStr.empty()) x = std::stod(xStr);
            if (!yStr.empty()) y = std::stod(yStr);
            if (!zStr.empty()) z = std::stod(zStr);
            if (!yawStr.empty()) yaw = std::stod(yawStr);
            if (!pitchStr.empty()) pitch = std::stod(pitchStr);
            std::ostringstream cmd;
            cmd << "teleport " << x << " " << y << " " << z << " " << yaw << " " << pitch;
            std::string response = SendCommand(cmd.str());
            resultText = "{\"response\":\"" + EscapeJson(response) + "\"}";
        }
        else if (toolName == "set_camera") {
            double x = 0, y = 0, z = 0, yaw = 0, pitch = 0, fov = 60;
            std::string xStr = ExtractJsonString(args, "x");
            std::string yStr = ExtractJsonString(args, "y");
            std::string zStr = ExtractJsonString(args, "z");
            std::string yawStr = ExtractJsonString(args, "yaw");
            std::string pitchStr = ExtractJsonString(args, "pitch");
            std::string fovStr = ExtractJsonString(args, "fov");
            if (!xStr.empty()) x = std::stod(xStr);
            if (!yStr.empty()) y = std::stod(yStr);
            if (!zStr.empty()) z = std::stod(zStr);
            if (!yawStr.empty()) yaw = std::stod(yawStr);
            if (!pitchStr.empty()) pitch = std::stod(pitchStr);
            if (!fovStr.empty()) fov = std::stod(fovStr);
            std::ostringstream cmd;
            cmd << "set_camera " << x << " " << y << " " << z << " " << yaw << " " << pitch << " " << fov;
            std::string response = SendCommand(cmd.str());
            resultText = "{\"response\":\"" + EscapeJson(response) + "\"}";
        }
        else if (toolName == "send_key") {
            std::string key = ExtractJsonString(args, "key");
            std::string action = ExtractJsonString(args, "action");
            if (action.empty()) action = "press";
            std::string response = SendCommand("input " + key + " " + action);
            resultText = "{\"response\":\"" + EscapeJson(response) + "\"}";
        }
        else if (toolName == "send_mouse") {
            int dx = ExtractJsonInt(args, "dx");
            int dy = ExtractJsonInt(args, "dy");
            int buttons = ExtractJsonInt(args, "buttons");
            int wheel = ExtractJsonInt(args, "wheel");
            std::ostringstream cmd;
            cmd << "mouse " << dx << " " << dy << " " << buttons << " " << wheel;
            std::string response = SendCommand(cmd.str());
            resultText = "{\"response\":\"" + EscapeJson(response) + "\"}";
        }
        else if (toolName == "set_time") {
            double hours = 0;
            std::string hoursStr = ExtractJsonString(args, "hours");
            if (!hoursStr.empty()) hours = std::stod(hoursStr);
            std::string response = SendCommand("time " + std::to_string(hours));
            resultText = "{\"response\":\"" + EscapeJson(response) + "\"}";
        }
        else if (toolName == "set_god_mode") {
            bool enabled = false;
            std::string enabledStr = ExtractJsonString(args, "enabled");
            if (enabledStr == "true") enabled = true;
            std::string response = SendCommand(std::string("god ") + (enabled ? "on" : "off"));
            resultText = "{\"response\":\"" + EscapeJson(response) + "\"}";
        }
        else if (toolName == "set_noclip") {
            bool enabled = false;
            std::string enabledStr = ExtractJsonString(args, "enabled");
            if (enabledStr == "true") enabled = true;
            std::string response = SendCommand(std::string("noclip ") + (enabled ? "on" : "off"));
            resultText = "{\"response\":\"" + EscapeJson(response) + "\"}";
        }
        else if (toolName == "give_item") {
            std::string type = ExtractJsonString(args, "type");
            std::string name = ExtractJsonString(args, "name");
            int count = ExtractJsonInt(args, "count");
            if (count <= 0) count = 1;
            std::ostringstream cmd;
            cmd << "give " << type << " " << name << " " << count;
            std::string response = SendCommand(cmd.str());
            resultText = "{\"response\":\"" + EscapeJson(response) + "\"}";
        }
        else if (toolName == "set_game_speed") {
            double mult = 1.0;
            std::string multStr = ExtractJsonString(args, "multiplier");
            if (!multStr.empty()) mult = std::stod(multStr);
            std::string response = SendCommand("speed " + std::to_string(mult));
            resultText = "{\"response\":\"" + EscapeJson(response) + "\"}";
        }
        else if (toolName == "freeze_game") {
            bool enabled = false;
            std::string enabledStr = ExtractJsonString(args, "enabled");
            if (enabledStr == "true") enabled = true;
            std::string response = SendCommand(std::string("freeze ") + (enabled ? "on" : "off"));
            resultText = "{\"response\":\"" + EscapeJson(response) + "\"}";
        }
        else if (toolName == "lua_state") {
            std::string target = ExtractJsonString(args, "target");
            if (target.empty()) target = "globals";
            std::string response = SendCommand("lua_state " + target);
            resultText = "{\"response\":\"" + EscapeJson(response) + "\"}";
        }
        else if (toolName == "lua_run") {
            std::string code = ExtractJsonString(args, "code");
            std::string response = SendCommand("lua_run " + code);
            resultText = "{\"response\":\"" + EscapeJson(response) + "\"}";
        }
        else if (toolName == "lua_get") {
            std::string name = ExtractJsonString(args, "name");
            std::string response = SendCommand("lua_get " + name);
            resultText = "{\"response\":\"" + EscapeJson(response) + "\"}";
        }
        else if (toolName == "lua_set") {
            std::string name = ExtractJsonString(args, "name");
            std::string value = ExtractJsonString(args, "value");
            std::string response = SendCommand("lua_set " + name + " " + value);
            resultText = "{\"response\":\"" + EscapeJson(response) + "\"}";
        }
        else if (toolName == "lua_call") {
            std::string func = ExtractJsonString(args, "function");

            // Extract args array
            std::vector<std::string> argValues;
            size_t argsArrayPos = args.find("\"args\"");
            if (argsArrayPos != std::string::npos) {
                size_t bracket = args.find('[', argsArrayPos);
                if (bracket != std::string::npos) {
                    size_t endBracket = args.find(']', bracket);
                    std::string arrayContent = args.substr(bracket + 1, endBracket - bracket - 1);
                    size_t pos = 0;
                    while (pos < arrayContent.size()) {
                        size_t comma = arrayContent.find(',', pos);
                        std::string valStr = arrayContent.substr(pos, comma - pos);
                        valStr.erase(0, valStr.find_first_not_of(" \t\n\r\""));
                        valStr.erase(valStr.find_last_not_of(" \t\n\r\"") + 1);
                        if (!valStr.empty()) {
                            argValues.push_back(valStr);
                        }
                        if (comma == std::string::npos) break;
                        pos = comma + 1;
                    }
                }
            }

            std::ostringstream cmd;
            cmd << "lua_call " << func;
            for (const auto& a : argValues) {
                cmd << " " << a;
            }
            std::string response = SendCommand(cmd.str());
            resultText = "{\"response\":\"" + EscapeJson(response) + "\"}";
        }
        else if (toolName == "lua_safe") {
            std::string code = ExtractJsonString(args, "code");
            std::string response = SendCommand("lua_safe " + code);
            resultText = "{\"response\":\"" + EscapeJson(response) + "\"}";
        }
        else if (toolName == "trace_calls") {
            int address = ExtractJsonInt(args, "address");
            int maxLogs = ExtractJsonInt(args, "max_logs");
            if (maxLogs <= 0) maxLogs = 100;
            std::string response = SendCommand("trace_calls " + Hex(address) + " " + std::to_string(maxLogs));
            resultText = "{\"response\":\"" + EscapeJson(response) + "\"}";
        }
        else if (toolName == "vtable") {
            int address = ExtractJsonInt(args, "address");
            int depth = ExtractJsonInt(args, "depth");
            if (depth <= 0) depth = 10;
            std::string response = SendCommand("vtable " + Hex(address) + " " + std::to_string(depth));
            resultText = "{\"response\":\"" + EscapeJson(response) + "\"}";
        }
        else if (toolName == "find_vtables") {
            int start = ExtractJsonInt(args, "start");
            int end = ExtractJsonInt(args, "end");
            std::string response = SendCommand("find_vtables " + Hex(start) + " " + Hex(end));
            resultText = "{\"response\":\"" + EscapeJson(response) + "\"}";
        }
        else if (toolName == "strings_ref") {
            int address = ExtractJsonInt(args, "address");
            std::string response = SendCommand("strings_ref " + Hex(address));
            resultText = "{\"response\":\"" + EscapeJson(response) + "\"}";
        }
        else if (toolName == "alloc_track") {
            bool enabled = false;
            std::string enabledStr = ExtractJsonString(args, "enabled");
            if (enabledStr == "true") enabled = true;
            std::string response = SendCommand(std::string("alloc_track ") + (enabled ? "on" : "off"));
            resultText = "{\"response\":\"" + EscapeJson(response) + "\"}";
        }
        else if (toolName == "heap_walk") {
            std::string response = SendCommand("heap_walk");
            resultText = "{\"response\":\"" + EscapeJson(response) + "\"}";
        }
        else if (toolName == "seh_chain") {
            std::string response = SendCommand("seh_chain");
            resultText = "{\"response\":\"" + EscapeJson(response) + "\"}";
        }
        else if (toolName == "perf_sample") {
            int count = ExtractJsonInt(args, "count");
            int interval = ExtractJsonInt(args, "interval_ms");
            if (count <= 0) count = 1000;
            if (interval <= 0) interval = 1;
            std::string response = SendCommand("perf_sample " + std::to_string(count) + " " + std::to_string(interval));
            resultText = "{\"response\":\"" + EscapeJson(response) + "\"}";
        }
        else if (toolName == "mem_diff") {
            int address = ExtractJsonInt(args, "address");
            int size = ExtractJsonInt(args, "size");
            int snapId = ExtractJsonInt(args, "snapshot_id");
            std::string response = SendCommand("mem_diff " + Hex(address) + " " + std::to_string(size) + " " + std::to_string(snapId));
            resultText = "{\"response\":\"" + EscapeJson(response) + "\"}";
        }
        else if (toolName == "signature") {
            int address = ExtractJsonInt(args, "address");
            int length = ExtractJsonInt(args, "length");
            std::string response = SendCommand("signature " + Hex(address) + " " + std::to_string(length));
            resultText = "{\"response\":\"" + EscapeJson(response) + "\"}";
        }
        else {
            return MakeResponse(id, "\"Unknown tool: " + toolName + "\"", true);
        }

        return MakeResponse(id, MakeTextResult(resultText));
    }
    else {
        return MakeResponse(id, "\"Unknown method: " + method + "\"", true);
    }
}

// The MCP servers run only when the player has turned them on: [Mcp] Enabled=1 in the rfa_mods.ini
// beside this program (off by default).
bool McpEnabled() {
    wchar_t path[MAX_PATH] = {};
    DWORD length = GetModuleFileNameW(nullptr, path, MAX_PATH);
    if (length == 0 || length >= MAX_PATH) return false;
    wchar_t* slash = wcsrchr(path, L'\\');
    if (!slash) return false;
    wcscpy_s(slash + 1, MAX_PATH - (slash + 1 - path), L"rfa_mods.ini");
    return GetPrivateProfileIntW(L"Mcp", L"Enabled", 0, path) != 0;
}

int main() {
    // Disable buffering for stdio
    setvbuf(stdin, NULL, _IONBF, 0);
    setvbuf(stdout, NULL, _IONBF, 0);
    setvbuf(stderr, NULL, _IONBF, 0);

    if (!McpEnabled()) {
        fprintf(stderr, "RFA-Mod's MCP servers are off. To use them, set Enabled=1 under [Mcp] in rfa_mods.ini.\n");
        return 1;
    }

    fprintf(stderr, "RFA Live Analysis MCP Server starting...\n");

    // Try to connect
    if (!ConnectToPipe()) {
        fprintf(stderr, "Warning: Could not connect to pipe. Will retry on first request.\n");
    } else {
        fprintf(stderr, "Connected to RFA Live Analysis pipe\n");
    }

    // Read JSON-RPC requests from stdin line by line
    char buffer[65536];
    while (fgets(buffer, sizeof(buffer), stdin)) {
        std::string request(buffer);
        // Trim trailing newline
        while (!request.empty() && (request.back() == '\n' || request.back() == '\r')) {
            request.pop_back();
        }
        if (request.empty()) continue;

        // Nothing a request holds may end the server: a bad number or similar becomes an error answer.
        std::string response;
        try {
            response = HandleRequest(request);
        } catch (const std::exception& e) {
            response = MakeResponse(RequestId(request), "\"" + EscapeJson(std::string("Internal error: ") + e.what()) + "\"", true);
        } catch (...) {
            response = MakeResponse(RequestId(request), "\"Internal error\"", true);
        }
        if (response.empty()) continue;
        printf("%s\n", response.c_str());
        fflush(stdout);
    }

    // Cleanup
    std::lock_guard<std::mutex> lock(g_pipeMutex);
    if (g_pipe != INVALID_HANDLE_VALUE) {
        CloseHandle(g_pipe);
    }

    return 0;
}