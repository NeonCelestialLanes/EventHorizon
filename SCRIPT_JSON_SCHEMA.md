# Script JSON

Schema version: **1**

A library script is one JSON object. A script nested in an event uses the same fields. `schemaVersion` is required on a file you import by itself, and optional on a script inside an event.

Property names are camelCase. Category values use the same spelling rules as the other schemas: PascalCase is canonical, and spaces, hyphens, underscores, and letter case are accepted on import.

## Required

| Field | Type |
| --- | --- |
| `schemaVersion` | number `1` on a standalone file |
| `scriptId` | string, identifier rules in `EVENT_JSON_SCHEMA.md` |
| `title` | string |

`spokenText` may be an empty string.

## Fields

| Field | Type | Notes |
| --- | --- | --- |
| `category` | enum | See below. Default `Custom` |
| `spokenText` | string | What the host reads aloud |
| `stageDirections` | string | Shown apart from the spoken text |
| `announcement` | string | Short text the host can copy. It is never posted to chat |
| `eventId` | string | Optional. Omit on a library script |
| `activityId` | string | Optional link |
| `sourceScriptId` | string | Set when an event script was copied from the library |
| `hostNotes` | string | |

## Categories

`Welcome`, `DjIntroduction`, `DjTransition`, `GameIntroduction`, `GameInstructions`, `GameCommentary`, `WinnerAnnouncement`, `Giveaway`, `GeneralAnnouncement`, `Closing`, `Custom`.

## Reuse

A library script can be copied onto any number of events. Each copy gets its own `scriptId` and can be edited without changing the library script or any other event.

The reader can move to the previous or next script, search, copy the spoken text, copy the announcement, and mark a script read. Read marks are live progress, not a field in this file.

## Example

See `Examples/script-welcome.json`.
