# Event Horizon

Event Horizon is a Dalamud plugin for planning an FFXIV event and hosting it live. Parties, games, scripts, and prizes live in JSON files. A new event or a new party game does not need a new build when it fits the schema.

The plugin does not post chat, open links, trade, or play the game for you. Scores, reveals, and schedule progress are manual.

## Requirements

- Dalamud API 15
- .NET 10
- Dalamud.NET.Sdk 15.0.0, the same SDK used by the other local plugins

## Build

The copy Dalamud reloads in development is the project build, not the solution build:

```powershell
dotnet build EventHorizon.csproj -c Debug
```

That writes `bin\Debug\EventHorizon.dll` and copies `EventHorizon.json` next to it. A solution build (`EventHorizon.sln`) writes `bin\x64\Debug` or `bin\x64\Release` instead, and Dalamud will not see that file unless the dev plugin path points there.

Check the sample files and the host logic without the game:

```powershell
dotnet run --project EventHorizon.Verify -c Debug
```

## Install from Dalamud

This is a custom plugin repo. Add it once under Dalamud **Experimental**, then install **Event Horizon** from `/xlplugins` like any other plugin.

1. In game, type `/xlsettings` (or open **Dalamud Settings** from the plugin installer).
2. Open the **Experimental** tab.
3. Skip **DevPlugins**. Go to **Custom Plugin Repositories**.
4. Paste this URL into the empty box at the bottom:

```
https://raw.githubusercontent.com/NeonCelestialLanes/EventHorizon/main/pluginmaster.json
```

5. Click **+**, leave the new row checked, then click the **Save** icon (bottom right).
6. Type `/xlplugins`, find **Event Horizon**, and install it.
7. Type `/eh` to open it.

## Make your own party

Start from [`Examples/party.json`](Examples/party.json). It is a short party with a venue, a welcome, one game, a closing, and `/yell` lines you can copy in game.

1. Copy that file and give the copy a new name.
2. Change `eventId` to a new id, then change the name, date, times, venue, and spoken lines.
3. In the plugin, use **Import event** and pick your file.
4. Open it from the dashboard.

[`Examples/event-starlight.json`](Examples/event-starlight.json) is a longer sample night, with DJs, several games, a contest, and prizes. Copy pieces from it when you need them. A full field list is in `EVENT_JSON_SCHEMA.md`.

## Install a local build

1. Build the project in Debug, as above.
2. Point Dalamud's dev plugin path at `bin\Debug\EventHorizon.dll`, or copy `EventHorizon.dll` and `EventHorizon.json` into `%AppData%\XIVLauncher\devPlugins\EventHorizon\`.
3. In Dalamud settings, enable dev plugins and turn Event Horizon on. Automatic reloading picks up a new DLL. If the window still looks old, turn the plugin off and on, then type `/eh`.

Dalamud stores this plugin's files in its config directory for `EventHorizon`. Open it from Settings → Open data folder. The layout is:

```text
EventHorizon/
  config.json
  Events/
  GameLibrary/
  ScriptLibrary/
  State/
  Backups/
  Notes/
```

The first launch copies the bundled examples into that folder if it has not been seeded before. They are sample data. Delete or archive them whenever you want.

## Commands

| Command | What it does |
| --- | --- |
| `/eventhorizon` or `/eh` | Open or close the main window |
| `/eh host` | Open Live Host for the selected event. If nothing is selected, it says so. |
| `/eh next` | Advance the current activity. If nothing is selected, it says so. |
| `/eh reload` | Reload the selected event file. Live progress is kept. Unsaved edits in that file are discarded. |
| `/eh games` | Open the Game Library |
| `/eh schedule` | Open the selected event's schedule |

## How a night works

1. Type `/eh`.
2. Open an event from the dashboard, or import one.
3. Review the schedule, DJs, scripts, games, prizes, and notes.
4. Press **Enter Live Host Mode** or **Start hosting**.
5. Read the script, reveal answers yourself, record winners, and press **Next** when you are ready.

The clock can suggest which activity looks current. It never marks one complete. When the next announcement or party game is due, a banner flashes and a short chime plays. **Go to it** starts that block. **Quiet** silences that cue.

**Copy announcement** walks the `/yell` lines for the section you are on. Each press copies the next line, then it starts over. The fraction stays on the button. It is not added to the pasted text.

**Teleport** sends you to the venue's world, district, ward, and plot through Lifestream. Lifestream has to be enabled. A venue with no ward and plot does not teleport.

On a tournament game, add the players, set how many sit at a table, and press **Make bracket**. Click a name when that table finishes. The plugin records the winner. It does not deal cards.

**Reset party** clears live progress and puts the event back to Ready. The schedule, scripts, and games stay. It asks first and keeps a backup.

## Live sync

On the dashboard or in Settings, press **Create sync code** and give that code to the other planners. They paste it into **Join with a code**.

The short code works on the same network. **Copy address** is the longer line for a planner who is somewhere else. Windows may ask to allow Event Horizon on private networks. Allow it, or the other planner cannot connect.

Edits go both ways: the schedule, scripts, games, bracket, scores, and the live show. The latest save is what everyone sees. Chat is never posted.

## ChatGPT

1. Give ChatGPT `EVENT_JSON_SCHEMA.md`, `GAME_JSON_SCHEMA.md`, or `SCRIPT_JSON_SCHEMA.md`, plus one of the files in `Examples/`.
2. Ask for a new file that uses only those fields.
3. Download the JSON.
4. In the plugin, use Import event, Import game, or Import script.
5. The plugin checks the schema version, required fields, ids, and times. A bad file is reported and not imported. Replacing an existing id asks first and writes a backup.

A useful request:

```text
Write an Event Horizon event JSON file, schemaVersion 1.
Use only fields from EVENT_JSON_SCHEMA.md.
Ids must match ^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$.
Date is yyyy-MM-dd. Times are HH:mm in timezone America/New_York.
Do not invent plugin features. Games must use the generic formats.
Return one JSON object and nothing else.
```

Game templates and scripts can be imported on their own. Copy a library script or game onto an event when you want an editable copy. Editing that copy does not change the library. Deleting a library template does not remove copies already on events.

## Themes

Midnight Neon is the default. Afterword and NCL Arcade are included. Accent color, UI scale, compact spacing, script text size, and the motion toggle apply immediately.

## What stays out of the event file

Live progress is `State/<eventId>.state.json`: the current activity, revealed answers, scores, winners, prize status, revealed clues, and quick notes. Reloading or replacing an event file does not delete that progress. If the new file drops an id that progress still uses, the plugin warns you and waits. Nothing is overwritten silently.

## Schema

The version this build reads is **1**. Formal schemas are in `schemas/`. The markdown guides next to this file describe the same models.
