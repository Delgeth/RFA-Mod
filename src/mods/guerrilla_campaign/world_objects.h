// Guerrilla's campaign markers (activity nodes, safehouses, raid nodes, ...): classes Armageddon
// has no code for, so the game never loads them. The world converter writes them, with their
// properties, to mods\guerrilla_campaign\world\campaign_objects.xml:
//   <objects><object><class>object_demolitions_master_node</class><handle>7A030003</handle>
//   <parent>7A030005</parent><zone>rfg_07_10</zone><pos>x y z</pos>
//   <property><name>demolitions_master_type</name><value>je_dm_01</value></property>...
//   </object>...</objects>
#pragma once
#include "campaign.h"
#include <string>
#include <utility>
#include <vector>

namespace campaign::world {

struct Object {
    std::string objectClass;
    unsigned handle = 0xFFFFFFFF;
    unsigned parent = 0xFFFFFFFF;  // the object it is attached to (a demolitions node's building)
    std::string zone;
    Vector pos{};
    bool hasParentPos = false;     // where the parent is in Guerrilla's world (parent_pos): to find it by its place
    Vector parentPos{};            // when ours gave it another handle
    std::vector<std::pair<std::string, std::string>> properties;

    std::string Property(const char* name, const char* fallback = "") const;
};

// Reads the file (once; false and a line in the log if it isn't there).
bool Load();
// Every object of a class.
std::vector<const Object*> OfClass(const char* objectClass);
const Object* Find(unsigned handle);

}  // namespace campaign::world
