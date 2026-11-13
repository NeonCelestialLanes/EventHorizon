# Game JSON

Schema version: **1**

A library game is one JSON object in `GameLibrary/`. A game inside an event uses the same shape with `instanceId` instead of `templateId`. See `EVENT_JSON_SCHEMA.md` for shared rules: camelCase, identifier shape, times, and enum spelling.

There is no built-in list of party games. Send or Delete, trivia, a bracket, and a word collection are all the same object with different `formats` and `rounds`.

## Required

| Field | Type |
| --- | --- |
| `schemaVersion` | number `1` |
| `templateId` | string |
| `name` | string |

An event copy requires `instanceId` instead, and does not require `schemaVersion`.

## Fields

| Field | Type | Notes |
| --- | --- | --- |
| `description` | string | |
| `category` | string | Free text, used by the library filter |
| `formats` | array | One or more of the format enums. They combine |
| `rules` | string | Shown to the host |
| `introScript`, `closingScript` | string | Spoken text stored on the game |
| `defaultPrize` | string | Library only. Event copies use `prizeId` |
| `prizeId` | string | Event copies |
| `scoringMethod` | string | Free text, such as `Points` or `Manual` |
| `participationMethod` | string | Free text, such as `Open` or `Sign up` |
| `hostNotes` | string | |
| `rounds` | array | May be empty |
| `tournament` | object | Used when a format is `Tournament` |

## Formats

`MultipleChoice`, `QuestionAnswer`, `Elimination`, `Points`, `Tournament`, `Voting`, `Collection`, `Custom`.

Examples that must be expressible, and are not special code:

| Game | Formats | What the rounds hold |
| --- | --- | --- |
| Send or Delete | `MultipleChoice`, and `Points` or `Elimination` if you want them | `choices` of `SEND` and `DELETE`, `correctAnswer`, the post in `extras.chirp` |
| Two Truths and a Chirp | `MultipleChoice` | Three posts in `choices`, the invented one in `correctAnswer` |
| Trivia | `QuestionAnswer` and/or `MultipleChoice`, plus `Points` | `prompt`, optional `choices`, `correctAnswer` |
| Uno, or any table tournament | `Tournament` | Players and matches. No card rules |
| Collect the Tags | `Collection` | One hidden word per round in `prompt` |

A new game uses the same fields. Put game-specific labels in `extras`. The host view prints each extra as a labeled line. It does not look for a particular game name.

## Rounds

Every field except `roundId` is optional. Do not fill a field the round does not use.

| Field | Type |
| --- | --- |
| `roundId` | string, unique within the game |
| `title`, `prompt`, `description` | string |
| `choices` | array of strings |
| `correctAnswer` | string |
| `explanation` | string |
| `sourceReference` | string |
| `featuredStaff` | string |
| `imagePath` | string, shown as a path, not drawn |
| `hostNotes` | string |
| `announcement` | string, copied by the host, never posted |
| `extras` | object of string values |

`correctAnswer` stays hidden until the host presses Reveal.

## Tournament

```json
{ "format": "SingleElimination", "seats": 4, "matches": [] }
```

`seats` is how many players sit at one table, from 2 to 8. A missing value means 2.

Match: `matchId`, `round` (number, starting at 1), `players`, `playerA`, `playerB`, `nextMatchId`, `nextSeat`, `winner`, `notes`.

`playerA` and `playerB` repeat the first two `players` so older files still read. `nextMatchId` is the later table that this winner feeds, and `nextSeat` is that table's seat, starting at 0.

Make bracket seats the player list and draws the later rounds empty. Clicking a name records that table's winner in live progress. The bracket does not deal cards or play the game. A winner recorded while hosting overrides `winner` on the match.

## Players

Not part of the template. The host adds names, scores, and eliminated flags during the event. Those values are stored in live progress for that event's `instanceId`.

## Library bundle

Import game library accepts either a JSON array of templates or:

```json
{ "schemaVersion": 1, "games": [ ] }
```

## Adding a template to an event

The plugin copies the template onto the event with a new `instanceId`. Later edits to the copy do not change the template. Deleting the template does not remove the copy. The same template can be added more than once.

## Example files

- `Examples/game-send-or-delete.json`
- `Examples/game-two-truths.json`
- `Examples/game-trivia.json`
- `Examples/game-uno-tournament.json`
- `Examples/game-collect-the-tags.json`
