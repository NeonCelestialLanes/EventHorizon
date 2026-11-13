# Event JSON

Schema version: **1**

An event file is one JSON object. Property names are camelCase. The plugin writes enums as PascalCase and accepts the same names with any capitalization, spaces, hyphens, or underscores (`DJ Set` and `DjSet` are both `DjSet`). Unknown properties are ignored. They are not stored.

Files may include comments and trailing commas. Those are not written back.

## Required

| Field | Type | Rule |
| --- | --- | --- |
| `schemaVersion` | number | Must be `1` |
| `eventId` | string | Unique. See identifier rules |
| `eventName` | string | Not blank |
| `date` | string | `yyyy-MM-dd` |

## Event fields

| Field | Type | Notes |
| --- | --- | --- |
| `description` | string | |
| `timezone` | string | IANA name. Default `America/New_York`. Example: `America/Chicago` |
| `startTime`, `endTime` | string | See times |
| `status` | enum | `Draft`, `Planned`, `Ready`, `Live`, `Completed`, `Archived` |
| `venue` | object | All of its fields are optional |
| `organizer` | string | |
| `staff` | array | |
| `schedule` | array | No fixed length |
| `djs` | array | |
| `games` | array | Event copies, not library templates |
| `scripts` | array | Event copies |
| `contests` | array | May run without a schedule block |
| `prizes` | array | |
| `notes` | array | |
| `checklists` | array | |

### Venue

`name`, `dataCenter`, `world`, `district`, `ward`, `plot`, `notes`. All strings. A house plot is optional. Leave `ward` and `plot` out for a club, cafe, or other room. `district` is the housing area, such as Mist or Lavender Beds. Teleport also notices that name in the venue notes.

### Staff

| Field | Type |
| --- | --- |
| `staffId` | string, required when the object is present |
| `name` | string |
| `role` | string |
| `notes` | string |

## Schedule

| Field | Type | Notes |
| --- | --- | --- |
| `activityId` | string | Required, unique in the schedule |
| `startTime`, `endTime` | string | Optional |
| `title` | string | |
| `description` | string | |
| `category` | enum | `Welcome`, `Announcement`, `DjSet`, `Game`, `Contest`, `Giveaway`, `Break`, `Intermission`, `Closing`, `Custom` |
| `djId` | string | Optional reference |
| `gameInstanceId` | string | Optional reference |
| `scriptId` | string | Optional reference |
| `prizeId` | string | Optional reference |
| `hostNotes` | string | |
| `status` | enum | Optional starting status only: `Scheduled`, `Live`, `Completed`, `Skipped`, `Delayed`, `Paused` |

A missing reference is a warning, not a failed import. Live hosting stores the real status in the state file and that copy wins after the event has been hosted.

The plugin never marks an activity complete because the clock reached its end.

## DJs

| Field | Type |
| --- | --- |
| `djId` | string |
| `name` | string |
| `startTime`, `endTime` | string |
| `streamUrl` | string, shown and copyable, never opened |
| `introScriptId`, `transitionScriptId` | string |
| `introText`, `transitionText` | string, spoken copy stored on the DJ |
| `notes` | string |

## Games on the event

These are independent copies. See `GAME_JSON_SCHEMA.md` for rounds, formats, and tournaments. On an event, use `instanceId` instead of `templateId` as the required id. `templateId` is an optional note of which library game it came from.

| Field | Type |
| --- | --- |
| `instanceId` | string, required |
| `templateId` | string |
| `name` | string |
| `description`, `category` | string |
| `formats` | array of game format enums |
| `rules`, `introScript`, `closingScript` | string |
| `prizeId` | string |
| `scoringMethod`, `participationMethod` | string, free text |
| `hostNotes` | string |
| `rounds` | array |
| `tournament` | object |

Player lists, the current round, and revealed answers are live progress. They do not belong in this file.

## Scripts on the event

Same fields as `SCRIPT_JSON_SCHEMA.md`. `schemaVersion` is optional on a script nested inside an event. `sourceScriptId` may name the library script it was copied from.

## Contests

| Field | Type | Notes |
| --- | --- | --- |
| `contestId` | string | |
| `title` | string | |
| `contestType` | string | Free text. `Collection`, `Screenshot`, `GPose`, `Scavenger`, `Raffle`, `Creative`, and `Custom` are conventional |
| `rules` | string | |
| `startTime`, `deadline` | string | |
| `prizeId` | string | |
| `submissionMethod`, `winnerMethod` | string | Free text. Winner selection is manual in the plugin |
| `winner` | string | Optional planned winner. A winner entered while hosting is stored in live progress |
| `notes` | string | |
| `clues` | array | Hidden until the host reveals them |

Clue: `clueId`, `content`, `revealTime`, `hostNotes`. `revealTime` is a reminder. The clue is not revealed by the clock.

## Prizes

| Field | Type | Notes |
| --- | --- | --- |
| `prizeId` | string | |
| `gameInstanceId`, `contestId`, `activityId` | string | Optional links |
| `rewardType` | enum | `Gil`, `AetherCoins`, `Item`, `Custom` |
| `rewardName` | string | |
| `amount` | number | Whole or decimal |
| `allowMultipleWinners` | boolean | |
| `status` | enum | Optional start: `NotAwarded`, `Pending`, `Awarded` |
| `winners` | string array | Optional planned winners. Hosting updates live progress |
| `notes` | string | |

The plugin does not mail items or transfer gil.

## Notes and checklists

Note: `noteId`, `title`, `category`, `body`.

Checklist item: `itemId`, `text`, `done`. Ticks made during an event are also kept in live progress.

## Times

A time is one of:

- `HH:mm` or `HH:mm:ss`, on the event `date`, in `timezone`
- `yyyy-MM-ddTHH:mm` or with seconds, still in the event timezone
- A full timestamp with `Z` or a numeric offset, which is an absolute instant

Daylight saving is handled with the timezone database on this computer. A time that falls in the spring gap is moved forward one hour and reported. A time that happens twice in the fall uses the earlier offset and is reported.

## Identifiers

`eventId` and every nested id must match:

```text
^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$
```

Ids are unique inside their own list. The same id may exist in two different lists. A duplicated id inside one list blocks the import.

## Import

1. The file is parsed. Malformed JSON is shown and not imported.
2. `schemaVersion` must be 1.
3. Required fields, ids, and times are checked.
4. If `eventId` already exists, the plugin asks before replacing. A backup is written first.
5. Import as copy gives the event a new id. Live progress is not copied.
6. After a replace or reload, live progress is kept. If it mentions ids the new file removed, you are asked before those references are dropped.

## Live progress

Not part of this file. `State/<eventId>.state.json` holds hosting: current activity, round index, revealed round ids, players, scores, match winners, prize status, contest winners, checklist ticks, revealed clue ids, read scripts, and quick notes.

## Example

See `Examples/event-starlight.json`.
