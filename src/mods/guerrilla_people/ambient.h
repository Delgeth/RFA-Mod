// Guerrilla's walkers (ambient.cpp), run by guerrilla_people.cpp.
#pragma once

// Reads mods\guerrilla_people\ambient.txt and crews.txt and writes in the log what it found; at start-up.
void StartAmbient();
// Each frame on the game's thread (from the gameplay update): `ours` is whether the level is one of our worlds.
void AmbientFrame(bool ours);
