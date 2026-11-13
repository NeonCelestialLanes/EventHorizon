using System.Numerics;
using Dalamud.Bindings.ImGui;
using EventHorizon.Hosting;
using EventHorizon.Models;
using EventHorizon.Persistence;
using EventHorizon.Theme;

namespace EventHorizon.Ui;

internal static class LibraryPanel
{
    public static void DrawGames(HorizonApp app, MainWindow window)
    {
        Chrome.Section("Game library");
        Chrome.Muted("Templates are reusable. Adding one to an event copies it, so edits there stay on that event.");
        if (Chrome.Accent("Create game"))
        {
            var created = app.Store.CreateGame();
            app.Session.LibraryGameId = created.TemplateId;
            app.Note("Created a game template.");
        }

        ImGui.SameLine();
        if (Chrome.Button("Import game"))
            window.Pick("Import game", path => app.BeginImport("game", path));
        ImGui.SameLine();
        if (Chrome.Button("Import library"))
            window.Pick("Import game library", path => app.BeginImport("library", path));
        ImGui.SameLine();
        if (Chrome.Button("Export library"))
            window.SaveAs("Export game library", "game-library.json", path =>
            {
                app.Store.ExportLibrary(path);
                app.Note("Exported the game library.");
            });

        var gameSearch = app.Session.GameSearch;
        ImGui.SetNextItemWidth(220);
        if (ImGui.InputText("Search", ref gameSearch, 80))
            app.Session.GameSearch = gameSearch;
        ImGui.SameLine();
        ImGui.SetNextItemWidth(180);
        if (ImGui.BeginCombo("Category", app.Session.GameCategory))
        {
            foreach (var category in Categories(app))
            {
                if (ImGui.Selectable(category, category == app.Session.GameCategory))
                    app.Session.GameCategory = category;
            }

            ImGui.EndCombo();
        }

        var games = FilteredGames(app).ToList();
        ImGui.BeginChild("game-list", new Vector2(240, 0), true);
        foreach (var game in games)
        {
            if (ImGui.Selectable(game.Name + "##" + game.TemplateId, app.Session.LibraryGameId == game.TemplateId))
                app.Session.LibraryGameId = game.TemplateId;
        }

        ImGui.EndChild();
        ImGui.SameLine();
        ImGui.BeginChild("game-editor", new Vector2(0, 0), false);
        var selected = games.FirstOrDefault(game => game.TemplateId == app.Session.LibraryGameId) ?? games.FirstOrDefault();
        if (selected is null)
            Chrome.Muted("No games match.");
        else
            DrawTemplate(app, window, selected);
        ImGui.EndChild();
    }

    public static void DrawScripts(HorizonApp app, MainWindow window)
    {
        Chrome.Section("Script library");
        Chrome.Muted("Library scripts can be copied onto any event. The copy is edited on its own.");
        if (Chrome.Accent("Create script"))
        {
            var created = app.Store.CreateScript();
            app.Session.LibraryScriptId = created.ScriptId;
        }

        ImGui.SameLine();
        if (Chrome.Button("Import script"))
            window.Pick("Import script", path => app.BeginImport("script", path));
        var scriptSearch = app.Session.ScriptSearch;
        ImGui.SetNextItemWidth(220);
        if (ImGui.InputText("Search##scripts", ref scriptSearch, 80))
            app.Session.ScriptSearch = scriptSearch;
        var scripts = FilteredScripts(app.Store.Scripts.Values, app.Session.ScriptSearch, app.Session.ScriptCategory).ToList();
        ImGui.BeginChild("script-list", new Vector2(240, 0), true);
        foreach (var script in scripts)
        {
            if (ImGui.Selectable(script.Title + "##" + script.ScriptId, app.Session.LibraryScriptId == script.ScriptId))
                app.Session.LibraryScriptId = script.ScriptId;
        }

        ImGui.EndChild();
        ImGui.SameLine();
        ImGui.BeginChild("script-editor", new Vector2(0, 0), false);
        var selected = scripts.FirstOrDefault(script => script.ScriptId == app.Session.LibraryScriptId) ?? scripts.FirstOrDefault();
        if (selected is null)
            Chrome.Muted("No scripts match.");
        else
            ScriptEditor.DrawLibrary(app, window, selected, scripts);
        ImGui.EndChild();
    }

    private static void DrawTemplate(HorizonApp app, MainWindow window, GameTemplate game)
    {
        ImGui.TextDisabled(game.TemplateId);
        if (Chrome.Required("Name", game.Name, value => game.Name = value))
            app.Store.MarkGame(game.TemplateId);
        if (Chrome.Field("Category", game.Category, value => game.Category = value))
            app.Store.MarkGame(game.TemplateId);
        if (Chrome.Area("Description", game.Description, value => game.Description = value, 50))
            app.Store.MarkGame(game.TemplateId);
        GameForms.Formats(game.Formats, () => app.Store.MarkGame(game.TemplateId));
        if (Chrome.Area("Rules", game.Rules, value => game.Rules = value, 60))
            app.Store.MarkGame(game.TemplateId);
        if (Chrome.Area("Introduction", game.IntroScript, value => game.IntroScript = value, 60))
            app.Store.MarkGame(game.TemplateId);
        if (Chrome.Area("Closing", game.ClosingScript, value => game.ClosingScript = value, 60))
            app.Store.MarkGame(game.TemplateId);
        if (Chrome.Field("Default prize", game.DefaultPrize, value => game.DefaultPrize = value))
            app.Store.MarkGame(game.TemplateId);
        if (Chrome.Field("Scoring", game.ScoringMethod, value => game.ScoringMethod = value))
            app.Store.MarkGame(game.TemplateId);
        if (Chrome.Field("Participation", game.ParticipationMethod, value => game.ParticipationMethod = value))
            app.Store.MarkGame(game.TemplateId);
        TellWordBox.Draw(game.TellWord, value => game.TellWord = value, () => app.Store.MarkGame(game.TemplateId));
        if (Chrome.Area("Host notes", game.HostNotes, value => game.HostNotes = value, 50))
            app.Store.MarkGame(game.TemplateId);
        GameForms.Rounds(app, game.Rounds, null, null, () => app.Store.MarkGame(game.TemplateId));
        if (Chrome.Button("Duplicate"))
        {
            var copy = app.Store.DuplicateGame(game);
            app.Session.LibraryGameId = copy.TemplateId;
        }

        ImGui.SameLine();
        if (Chrome.Button("Export"))
        {
            var id = game.TemplateId;
            window.SaveAs("Export game", id + ".json", path =>
            {
                app.Store.ExportGame(id, path);
                app.Note("Exported the game.");
            });
        }

        ImGui.SameLine();
        if (Chrome.Tone("Delete", ThemeHolder.Red))
        {
            var id = game.TemplateId;
            var name = game.Name;
            app.Ask("Delete the library template \"" + name + "\"? Events that already copied it are kept.", () =>
            {
                app.Store.DeleteGame(id);
                app.Session.LibraryGameId = null;
            });
        }

        var selected = app.Store.SelectedEvent();
        if (selected is not null && Chrome.Accent("Add to " + selected.EventName))
        {
            app.Store.AddGameToEvent(selected, game);
            app.Note("Added a separate copy to " + selected.EventName + ".");
        }
    }

    private static IEnumerable<string> Categories(HorizonApp app)
    {
        yield return "All";
        foreach (var category in app.Store.Games.Values.Select(game => game.Category).Where(category => !string.IsNullOrWhiteSpace(category)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(category => category))
            yield return category!;
    }

    private static IEnumerable<GameTemplate> FilteredGames(HorizonApp app) =>
        app.Store.Games.Values
            .Where(game => app.Session.GameCategory == "All" || string.Equals(game.Category, app.Session.GameCategory, StringComparison.OrdinalIgnoreCase))
            .Where(game => Matches(game.Name + " " + game.Description + " " + game.Category, app.Session.GameSearch))
            .OrderBy(game => game.Name, StringComparer.OrdinalIgnoreCase);

    public static IEnumerable<ScriptDocument> FilteredScripts(IEnumerable<ScriptDocument> scripts, string search, string category) =>
        scripts.Where(script => category == "All" || script.Category.ToString() == category)
            .Where(script => Matches(script.Title + " " + script.SpokenText + " " + script.Announcement, search))
            .OrderBy(script => script.Title, StringComparer.OrdinalIgnoreCase);

    private static bool Matches(string? haystack, string search) =>
        string.IsNullOrWhiteSpace(search) || (haystack ?? "").Contains(search.Trim(), StringComparison.OrdinalIgnoreCase);
}

internal static class EventGamePanel
{
    public static void Draw(HorizonApp app, EventDocument ev)
    {
        if (Chrome.Button("Create game on this event"))
        {
            ev.Games.Add(new GameInstance
            {
                InstanceId = Ids.Unique("gme-", ev.Games.Select(game => game.InstanceId)),
                Name = "New game",
                Formats = [GameFormat.Custom],
                Rounds = [new GameRound { RoundId = Ids.New("rnd-"), Title = "Round 1" }],
            });
            app.Store.MarkEvent(ev.EventId);
        }

        ImGui.SameLine();
        var templates = app.Store.Games.Values.OrderBy(game => game.Name).ToList();
        ImGui.SetNextItemWidth(220);
        var label = templates.FirstOrDefault(game => game.TemplateId == app.Session.LibraryGameId)?.Name ?? "From library";
        if (ImGui.BeginCombo("##add-template", label))
        {
            foreach (var template in templates)
            {
                if (ImGui.Selectable(template.Name, template.TemplateId == app.Session.LibraryGameId))
                {
                    app.Store.AddGameToEvent(ev, template);
                    app.Note("Added a copy of " + template.Name + ".");
                }
            }

            ImGui.EndCombo();
        }

        if (ev.Games.Count == 0)
        {
            Chrome.Muted("This event has no games yet.");
            return;
        }

        if (app.Session.InstanceId is null || ev.Games.All(game => game.InstanceId != app.Session.InstanceId))
            app.Session.InstanceId = ev.Games[0].InstanceId;

        foreach (var game in ev.Games)
        {
            ImGui.SameLine();
            if (ImGui.Selectable(game.Name + "##" + game.InstanceId, app.Session.InstanceId == game.InstanceId, ImGuiSelectableFlags.None, new Vector2(140, 24)))
                app.Session.InstanceId = game.InstanceId;
        }

        var selected = ev.Games.First(game => game.InstanceId == app.Session.InstanceId);
        ImGui.Spacing();
        if (ImGui.RadioButton("Edit", app.Session.EditingGame))
            app.Session.EditingGame = true;
        ImGui.SameLine();
        if (ImGui.RadioButton("Play", !app.Session.EditingGame))
            app.Session.EditingGame = false;
        if (!app.Session.EditingGame)
        {
            GameBoard.Draw(app, ev, selected);
            return;
        }

        ImGui.TextDisabled(selected.InstanceId + (selected.TemplateId is null ? "" : "  ·  from " + selected.TemplateId));
        if (Chrome.Required("Name", selected.Name, value => selected.Name = value))
            app.Store.MarkEvent(ev.EventId);
        if (Chrome.Field("Category", selected.Category, value => selected.Category = value))
            app.Store.MarkEvent(ev.EventId);
        if (Chrome.Area("Description", selected.Description, value => selected.Description = value, 50))
            app.Store.MarkEvent(ev.EventId);
        GameForms.Formats(selected.Formats, () => app.Store.MarkEvent(ev.EventId));
        if (Chrome.Area("Rules", selected.Rules, value => selected.Rules = value, 50))
            app.Store.MarkEvent(ev.EventId);
        if (Chrome.Area("Introduction", selected.IntroScript, value => selected.IntroScript = value, 50))
            app.Store.MarkEvent(ev.EventId);
        if (Chrome.Area("Closing", selected.ClosingScript, value => selected.ClosingScript = value, 50))
            app.Store.MarkEvent(ev.EventId);
        if (Chrome.Field("Prize id", selected.PrizeId, value => selected.PrizeId = value))
            app.Store.MarkEvent(ev.EventId);
        if (Chrome.Field("Scoring", selected.ScoringMethod, value => selected.ScoringMethod = value))
            app.Store.MarkEvent(ev.EventId);
        if (Chrome.Field("Participation", selected.ParticipationMethod, value => selected.ParticipationMethod = value))
            app.Store.MarkEvent(ev.EventId);
        TellWordBox.Draw(selected.TellWord, value => selected.TellWord = value, () => app.Store.MarkEvent(ev.EventId));
        if (Chrome.Area("Host notes", selected.HostNotes, value => selected.HostNotes = value, 40))
            app.Store.MarkEvent(ev.EventId);
        if (selected.Formats.Contains(GameFormat.Tournament))
            Chrome.Muted("Switch to Play to add players and make the bracket.");
        GameForms.Rounds(app, selected.Rounds, ev, selected, () => app.Store.MarkEvent(ev.EventId));
        if (Chrome.Tone("Remove from event", ThemeHolder.Red))
        {
            var id = selected.InstanceId;
            app.Ask("Remove this game from the event? The library template is kept.", () =>
            {
                ev.Games.RemoveAll(game => game.InstanceId == id);
                app.Session.InstanceId = null;
                app.Store.MarkEvent(ev.EventId);
            });
        }
    }
}

internal static class EventScriptPanel
{
    public static void Draw(HorizonApp app, MainWindow window, EventDocument ev)
    {
        if (Chrome.Button("New script"))
        {
            ev.Scripts.Add(new ScriptDocument
            {
                ScriptId = Ids.Unique("scr-", ev.Scripts.Select(script => script.ScriptId)),
                Title = "New script",
                SpokenText = "",
                EventId = ev.EventId,
            });
            app.Store.MarkEvent(ev.EventId);
        }

        ImGui.SameLine();
        if (ImGui.BeginCombo("Copy from library", "Choose"))
        {
            foreach (var script in app.Store.Scripts.Values.OrderBy(script => script.Title))
            {
                if (ImGui.Selectable(script.Title))
                {
                    var copy = app.Store.CopyScriptToEvent(ev, script);
                    app.Session.EventScriptId = copy.ScriptId;
                }
            }

            ImGui.EndCombo();
        }

        if (ev.Scripts.Count == 0)
        {
            Chrome.Muted("This event has no scripts yet.");
            return;
        }

        var scripts = LibraryPanel.FilteredScripts(ev.Scripts, app.Session.ScriptSearch, "All").ToList();
        var eventScriptSearch = app.Session.ScriptSearch;
        ImGui.SetNextItemWidth(220);
        if (ImGui.InputText("Search##event-scripts", ref eventScriptSearch, 80))
            app.Session.ScriptSearch = eventScriptSearch;
        if (app.Session.EventScriptId is null || scripts.All(script => script.ScriptId != app.Session.EventScriptId))
            app.Session.EventScriptId = scripts.FirstOrDefault()?.ScriptId;
        var selected = scripts.FirstOrDefault(script => script.ScriptId == app.Session.EventScriptId);
        if (selected is not null)
            ScriptEditor.DrawEvent(app, window, ev, selected, scripts);
    }
}

internal static class ScriptEditor
{
    public static void DrawLibrary(HorizonApp app, MainWindow window, ScriptDocument script, List<ScriptDocument> list)
    {
        DrawFields(script, () => app.Store.MarkScript(script.ScriptId), app, null, list);
        if (Chrome.Button("Duplicate"))
        {
            var copy = app.Store.DuplicateScript(script);
            app.Session.LibraryScriptId = copy.ScriptId;
        }

        ImGui.SameLine();
        if (Chrome.Button("Export"))
        {
            var id = script.ScriptId;
            window.SaveAs("Export script", id + ".json", path => app.Store.ExportScript(id, path));
        }

        ImGui.SameLine();
        if (Chrome.Tone("Delete", ThemeHolder.Red))
        {
            var id = script.ScriptId;
            app.Ask("Delete this library script? Event copies are kept.", () =>
            {
                app.Store.DeleteScript(id);
                app.Session.LibraryScriptId = null;
            });
        }
    }

    public static void DrawEvent(HorizonApp app, MainWindow window, EventDocument ev, ScriptDocument script, List<ScriptDocument> list)
    {
        _ = window;
        DrawFields(script, () => app.Store.MarkEvent(ev.EventId), app, ev, list);
        if (Chrome.Button("Save copy to library"))
        {
            var copy = JsonIo.Clone(script);
            copy.ScriptId = Ids.Unique("scr-", app.Store.Scripts.Keys);
            copy.SchemaVersion = Validation.SchemaVersion;
            copy.EventId = null;
            copy.ActivityId = null;
            copy.SourceScriptId = script.ScriptId;
            app.Store.Scripts[copy.ScriptId] = copy;
            app.Store.MarkScript(copy.ScriptId);
            app.Store.SaveDirty();
            app.Note("Saved a library copy.");
        }

        ImGui.SameLine();
        if (Chrome.Tone("Remove from event", ThemeHolder.Red))
        {
            var id = script.ScriptId;
            ev.Scripts.RemoveAll(item => item.ScriptId == id);
            app.Store.MarkEvent(ev.EventId);
        }
    }

    private static void DrawFields(ScriptDocument script, Action changed, HorizonApp app, EventDocument? ev, List<ScriptDocument> list)
    {
        var index = list.FindIndex(item => item.ScriptId == script.ScriptId);
        if (index > 0 && Chrome.Button("Previous script"))
            Select(app, ev, list[index - 1].ScriptId);
        ImGui.SameLine();
        if (index >= 0 && index < list.Count - 1 && Chrome.Button("Next script"))
            Select(app, ev, list[index + 1].ScriptId);
        ImGui.TextDisabled(script.ScriptId);
        if (Chrome.Required("Title", script.Title, value => script.Title = value))
            changed();
        var category = script.Category;
        if (Chrome.EnumBox("Category", ref category))
        {
            script.Category = category;
            changed();
        }

        if (!string.IsNullOrWhiteSpace(script.StageDirections))
        {
            ImGui.TextColored(ThemeHolder.Gold, "Stage");
            ImGui.TextWrapped(script.StageDirections);
        }

        if (Chrome.Area("Stage directions", script.StageDirections, value => script.StageDirections = value, 50))
            changed();
        var scriptScale = app.Store.Config.UiScale;
        ImGui.SetWindowFontScale(1f);
        var scriptFont = app.ScriptFace?.Push(scriptScale, app.Store.Config.ScriptFontScale) == true;
        if (!scriptFont)
            ImGui.SetWindowFontScale(scriptScale);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, ThemeHolder.Panel);
        ImGui.BeginChild("spoken-" + script.ScriptId, new Vector2(0, 220), true);
        ImGui.TextWrapped(string.IsNullOrWhiteSpace(script.SpokenText) ? " " : script.SpokenText);
        ImGui.EndChild();
        ImGui.PopStyleColor();
        if (scriptFont)
            app.ScriptFace?.Pop();
        ImGui.SetWindowFontScale(scriptScale);
        if (Chrome.RequiredArea("Spoken text", script.SpokenText, value => script.SpokenText = value, 120))
            changed();
        if (Chrome.Area("Chat announcement", script.Announcement, value => script.Announcement = value, 50))
            changed();
        if (Chrome.Area("Host notes", script.HostNotes, value => script.HostNotes = value, 40))
            changed();
        var prompts = ev is null
            ? ScriptSpeech.Paragraphs([script], script.ScriptId)
            : ScriptSpeech.Paragraphs(ev.Scripts, script.ScriptId);
        if (prompts.Count == 0)
        {
            if (Chrome.Button("Copy announcement##" + script.ScriptId))
                app.Fail("This script has no chat announcement.");
        }
        else
        {
            for (var paragraphIndex = 0; paragraphIndex < prompts.Count; paragraphIndex++)
            {
                var paragraph = prompts[paragraphIndex];
                for (var part = 0; part < paragraph.Parts.Count; part++)
                {
                    var caption = prompts.Count == 1 && paragraph.Parts.Count == 1
                        ? "Copy announcement"
                        : "Copy " + (paragraphIndex + 1) + (paragraph.Parts.Count == 1 ? "" : " part " + (part + 1));
                    if (!Chrome.Button(caption + "##lib-" + script.ScriptId + "-" + paragraphIndex + "-" + part))
                        continue;
                    ImGui.SetClipboardText(paragraph.Parts[part]);
                    app.Note("Copied.");
                }
            }
        }

        if (ev is not null)
        {
            ImGui.SameLine();
            var state = app.Store.StateFor(ev.EventId);
            var read = state.ReadScriptIds.Contains(script.ScriptId);
            if (Chrome.Button(read ? "Mark unread" : "Mark as read"))
            {
                if (read)
                    state.ReadScriptIds.Remove(script.ScriptId);
                else
                    state.ReadScriptIds.Add(script.ScriptId);
                app.Store.MarkState(ev.EventId);
            }

            if (read)
            {
                ImGui.SameLine();
                Chrome.Badge("Read", ThemeHolder.Mint);
            }
        }
    }

    private static void Select(HorizonApp app, EventDocument? ev, string scriptId)
    {
        if (ev is null)
            app.Session.LibraryScriptId = scriptId;
        else
            app.Session.EventScriptId = scriptId;
    }
}

internal static class GameForms
{
    public static void Formats(List<GameFormat> formats, Action changed)
    {
        ImGui.TextDisabled("Formats can be combined.");
        foreach (var format in Enum.GetValues<GameFormat>())
        {
            var enabled = formats.Contains(format);
            if (!ImGui.Checkbox(Labels.Spaced(format.ToString()) + "##" + format, ref enabled))
                continue;

            if (enabled)
                formats.Add(format);
            else
                formats.Remove(format);
            changed();
        }
    }

    public static void Rounds(HorizonApp app, List<GameRound> rounds, EventDocument? ev, GameInstance? instance, Action changed)
    {
        Chrome.Section("Rounds");
        if (Chrome.Button("Add round"))
        {
            rounds.Add(new GameRound
            {
                RoundId = Ids.Unique("rnd-", rounds.Select(round => round.RoundId)),
                Title = "Round " + (rounds.Count + 1),
            });
            changed();
        }

        string? remove = null;
        foreach (var round in rounds)
        {
            ImGui.PushID(round.RoundId);
            if (Chrome.Field("Title", round.Title, value => round.Title = value))
                changed();
            if (Chrome.Area("Prompt", round.Prompt, value => round.Prompt = value, 60))
                changed();
            if (Chrome.Area("Description", round.Description, value => round.Description = value, 40))
                changed();
            var choices = string.Join("\n", round.Choices);
            if (Chrome.RequiredArea("Choices, one per line", choices, value => round.Choices = Split(value), 70))
                changed();
            if (Chrome.Field("Correct answer", round.CorrectAnswer, value => round.CorrectAnswer = value))
                changed();
            if (Chrome.Area("Explanation", round.Explanation, value => round.Explanation = value, 40))
                changed();
            if (Chrome.Field("Featured staff", round.FeaturedStaff, value => round.FeaturedStaff = value))
                changed();
            if (Chrome.Field("Source", round.SourceReference, value => round.SourceReference = value))
                changed();
            if (Chrome.Field("Image path", round.ImagePath, value => round.ImagePath = value, 400))
                changed();
            if (Chrome.Area("Host notes", round.HostNotes, value => round.HostNotes = value, 40))
                changed();
            if (Chrome.Area("Announcement", round.Announcement, value => round.Announcement = value, 40))
                changed();
            round.Extras ??= new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var key in round.Extras.Keys.ToList())
            {
                var current = round.Extras[key];
                if (!Chrome.Field(key, current, value =>
                    {
                        if (string.IsNullOrWhiteSpace(value))
                            round.Extras.Remove(key);
                        else
                            round.Extras[key] = value;
                    }))
                    continue;

                changed();
            }

            var draftKey = "extra-" + round.RoundId;
            app.Session.Drafts.TryGetValue(draftKey, out var extraDraft);
            extraDraft ??= "";
            if (Chrome.Required("New field", extraDraft, value => app.Session.Drafts[draftKey] = value ?? "", 40))
                _ = extraDraft;
            ImGui.SameLine();
            if (Chrome.Button("Add field") && app.Session.Drafts.TryGetValue(draftKey, out var typed) && !string.IsNullOrWhiteSpace(typed))
            {
                round.Extras[typed.Trim()] = "";
                app.Session.Drafts[draftKey] = "";
                changed();
            }

            if (Chrome.Button("Remove round"))
                remove = round.RoundId;
            ImGui.Separator();
            ImGui.PopID();
        }

        if (remove is not null)
        {
            rounds.RemoveAll(round => round.RoundId == remove);
            changed();
        }

        _ = instance;
        _ = ev;
    }

    private static List<string> Split(string value) =>
        value.Replace("\r", "").Split('\n').ToList();
}
