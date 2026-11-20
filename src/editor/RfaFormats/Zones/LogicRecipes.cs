namespace RfaFormats.Zones;

/// <summary>
/// Ready-made state machines for the things missions do most (made from the game's own functions,
/// as the game's scripts use them): the blanks are filled in, and the state machine is added to a
/// script. "Self" in a recipe is the handler's object (the one it is attached to).
/// </summary>
public sealed class LogicRecipe
{
    public enum BlankKind { Object, Text, Number }

    /// <summary>Something to fill in: an object (of the classes listed, most fitting first), a text or a number.</summary>
    public sealed record Blank(string Key, string Label, BlankKind Kind, IReadOnlyList<string> Classes, string Default = "", string Tip = "");

    public string Name { get; }
    public string Description { get; }
    public IReadOnlyList<Blank> Blanks { get; }
    readonly Func<LogicScript, Filled, LogicObject> make;

    LogicRecipe(string name, string description, IReadOnlyList<Blank> blanks, Func<LogicScript, Filled, LogicObject> make)
    {
        Name = name;
        Description = description;
        Blanks = blanks;
        this.make = make;
    }

    /// <summary>The blanks as filled in: objects by their handle (FFFFFFFF none), texts, numbers.</summary>
    public sealed class Filled
    {
        public Dictionary<string, uint> Objects { get; } = [];
        public Dictionary<string, string> Texts { get; } = [];
        public Dictionary<string, float> Numbers { get; } = [];
        internal LogicValue Object(string key) => Objects.TryGetValue(key, out uint h) && h != 0xFFFFFFFF ? LogicValue.OfObject(h) : LogicValue.OfObject(0xFFFFFFFF);
        internal LogicValue Text(string key) => LogicValue.OfText(Texts.TryGetValue(key, out string? t) ? t : "");
        internal float Number(string key) => Numbers.TryGetValue(key, out float n) ? n : 0;
    }

    /// <summary>Adds the recipe's state machine (and any variable it needs) to a script.</summary>
    public LogicObject AddTo(LogicScript script, Filled filled)
    {
        LogicObject machine = make(script, filled);
        machine.Name.Text = UniqueName(machine.Name.Text, script);
        script.Objects.Add(machine);
        return machine;
    }

    static string UniqueName(string name, LogicScript script)
    {
        var taken = script.Objects.Select(o => o.Name.Text).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(name)) return name;
        for (int i = 2; ; i++)
        {
            string tail = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string made = (name.Length + tail.Length > 31 ? name[..(31 - tail.Length)] : name) + tail;
            if (!taken.Contains(made)) return made;
        }
    }

    // ---- Building blocks ------------------------------------------------------------------------

    static LogicCall Act(string function, params LogicValue[] args)
    {
        LogicCall call = LogicCall.NewAction(function);
        call.Arguments.AddRange(args);
        return call;
    }

    static LogicCondition When(string test, params LogicValue[] args) => Test(false, test, args);
    static LogicCondition Unless(string test, params LogicValue[] args) => Test(true, test, args);

    static LogicCondition Test(bool not, string test, LogicValue[] args)
    {
        LogicCall call = LogicCall.NewTest(test);
        call.Arguments.AddRange(args);
        return LogicCondition.Test(call, not);
    }

    static LogicCondition Always => When("ec_always_true");

    static void Go(LogicState from, LogicState to, LogicCondition when, float wait = 0)
    {
        LogicEdge edge = LogicObject.NewEdge(from, to);
        edge.Condition = when;
        edge.MinWait = edge.MaxWait = wait;
        from.Edges.Add(edge);
    }

    static LogicObject Machine(string name, out LogicState start)
    {
        LogicObject machine = LogicObject.NewStateMachine(name);
        start = machine.States[0];
        return machine;
    }

    static readonly string[] Triggers = ["trigger_region"];
    static readonly string[] Squads = ["object_squad_spawn_node"];
    static readonly string[] Conversations = ["object_prepared_audio"];

    static Blank TriggerBlank => new("trigger", "Trigger", BlankKind.Object, Triggers, Tip: "The trigger region the player walks into.");
    static Blank SquadBlank => new("squad", "Squad spawn node", BlankKind.Object, Squads);
    static Blank ObjectiveBlank => new("objective", "Objective", BlankKind.Text, [], "Mission_01A_Objective_A",
                                       "The objective's name, as missions' objectives are named in the game's text (e.g. Mission_01A_Objective_A).");

    // ---- The recipes ----------------------------------------------------------------------------

    public static IReadOnlyList<LogicRecipe> All { get; } =
    [
        new("Player enters a trigger: spawn a squad",
            "Waits for the player to be in the trigger, then spawns the squad once.",
            [TriggerBlank, SquadBlank],
            (_, f) =>
            {
                LogicObject m = Machine("SpawnOnTrigger", out LogicState start);
                LogicState spawn = m.AddState("Spawn");
                spawn.OnEnter.Add(Act("sf_spawn_squad", f.Object("squad")));
                Go(start, spawn, When("ec_object_in_trigger", LogicValue.OfPlayer(), f.Object("trigger")));
                return m;
            }),

        new("Kill a squad: complete an objective",
            "Spawns the squad and adds the objective; once the squad is dead, completes the objective.",
            [SquadBlank, ObjectiveBlank],
            (_, f) =>
            {
                LogicObject m = Machine("KillSquad", out LogicState start);
                start.OnEnter.Add(Act("sf_spawn_squad", f.Object("squad")));
                start.OnEnter.Add(Act("sf_mf_mission_objective_add", f.Text("objective")));
                LogicState done = m.AddState("Done");
                done.OnEnter.Add(Act("sf_mf_mission_objective_complete", f.Text("objective"), LogicValue.OfBoolean(true)));
                Go(start, done, Unless("ec_squad_is_alive", f.Object("squad")), 1);
                return m;
            }),

        new("Player enters a trigger: save a checkpoint",
            "Saves a checkpoint the first time the player is in the trigger.",
            [TriggerBlank],
            (_, f) =>
            {
                LogicObject m = Machine("Checkpoint", out LogicState start);
                LogicState save = m.AddState("Save");
                save.OnEnter.Add(Act("sf_mf_checkpoint_save"));
                Go(start, save, When("ec_object_in_trigger", LogicValue.OfPlayer(), f.Object("trigger")));
                return m;
            }),

        new("Player enters a trigger: play a conversation",
            "Plays the conversation (an object_prepared_audio) when the player is in the trigger, and waits for it to finish.",
            [TriggerBlank, new Blank("conversation", "Conversation", BlankKind.Object, Conversations)],
            (_, f) =>
            {
                LogicObject m = Machine("Conversation", out LogicState start);
                LogicState play = m.AddState("Play");
                play.OnEnter.Add(Act("sf_audio_play_conversation", f.Object("conversation")));
                LogicState done = m.AddState("Done");
                Go(start, play, When("ec_object_in_trigger", LogicValue.OfPlayer(), f.Object("trigger")));
                Go(play, done, When("ec_is_conversation_finished", f.Object("conversation")), 1);
                return m;
            }),

        new("Objective: reach a place",
            "Adds the objective; when the player is in the trigger, completes it.",
            [ObjectiveBlank, TriggerBlank],
            (_, f) =>
            {
                LogicObject m = Machine("ReachPlace", out LogicState start);
                start.OnEnter.Add(Act("sf_mf_mission_objective_add", f.Text("objective")));
                LogicState done = m.AddState("Done");
                done.OnEnter.Add(Act("sf_mf_mission_objective_complete", f.Text("objective"), LogicValue.OfBoolean(true)));
                Go(start, done, When("ec_object_in_trigger", LogicValue.OfPlayer(), f.Object("trigger")));
                return m;
            }),

        new("Objective done: mission success",
            "Ends the mission in success once the objective is complete.",
            [ObjectiveBlank],
            (_, f) =>
            {
                LogicObject m = Machine("MissionWon", out LogicState start);
                LogicState won = m.AddState("Won");
                won.OnEnter.Add(Act("sf_mf_mission_success"));
                Go(start, won, When("ec_mission_objective_completed", f.Text("objective")), 1);
                return m;
            }),

        new("Time limit: mission failure",
            "Fails the mission after a number of seconds, unless the objective is complete by then.",
            [ObjectiveBlank, new Blank("seconds", "Seconds", BlankKind.Number, [], "120"),
             new Blank("reason", "Failure reason", BlankKind.Text, [], "", "The failure text's name (may be empty).")],
            (_, f) =>
            {
                LogicObject m = Machine("TimeLimit", out LogicState start);
                LogicState failed = m.AddState("Failed");
                failed.OnEnter.Add(Act("sf_mf_mission_failure", f.Text("reason")));
                LogicState made = m.AddState("InTime");
                Go(start, made, When("ec_mission_objective_completed", f.Text("objective")));
                Go(start, failed, Always, f.Number("seconds"));
                return m;
            }),

        new("Squad dead: spawn the next squad",
            "Spawns the first squad; once it is dead, spawns the second (a simple wave).",
            [new Blank("first", "First squad", BlankKind.Object, Squads), new Blank("second", "Next squad", BlankKind.Object, Squads)],
            (_, f) =>
            {
                LogicObject m = Machine("Waves", out LogicState start);
                start.OnEnter.Add(Act("sf_spawn_squad", f.Object("first")));
                LogicState next = m.AddState("Next");
                next.OnEnter.Add(Act("sf_spawn_squad", f.Object("second")));
                Go(start, next, Unless("ec_squad_is_alive", f.Object("first")), 1);
                return m;
            }),
    ];
}
