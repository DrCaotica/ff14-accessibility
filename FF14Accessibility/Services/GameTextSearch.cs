using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using Lumina.Data.Structs.Excel;
using Lumina.Excel;

namespace FF14Accessibility.Services;

/// <summary>
/// Developer tool: searches every string column of every game sheet for a text
/// and writes the hits, each with its two neighbouring rows above and below, to
/// Desktop\FFXIV_TextSearch.txt. Used to find which sheet row a message the game
/// shows comes from (e.g. a toast), and which block of related rows it sits in.
/// Aufruf: /acc findtext &lt;Text&gt;
/// </summary>
public sealed class GameTextSearch
{
    private const int NeighbourRows = 2;
    private const int MaxHits       = 200;

    private readonly IDataManager _data;
    private readonly IFramework   _framework;
    private readonly TolkService  _tolk;
    private readonly IPluginLog   _log;
    private bool _running;

    /// <summary>Creates the search tool.</summary>
    public GameTextSearch(IDataManager data, IFramework framework, TolkService tolk, IPluginLog log)
    {
        _data      = data;
        _framework = framework;
        _tolk      = tolk;
        _log       = log;
    }

    /// <summary>
    /// Starts the search on a worker thread (all sheets take seconds to read) and
    /// announces the result on the framework thread when done.
    /// </summary>
    public void Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            _tolk.SpeakInterrupt(AccessibilityStrings.TextSearchNoQuery);
            return;
        }
        if (_running)
        {
            _tolk.SpeakInterrupt(AccessibilityStrings.TextSearchBusy);
            return;
        }

        _running = true;
        _tolk.SpeakInterrupt(AccessibilityStrings.TextSearchStarted(query));
        _log.Info($"[TextSearch] Suche nach '{query}' gestartet, Sprache {_data.Language}.");

        Task.Run(() =>
        {
            string message;
            try
            {
                message = RunSearch(query);
            }
            catch (Exception ex)
            {
                _log.Error($"[TextSearch] Abgebrochen: {ex}");
                message = AccessibilityStrings.TextSearchFailed;
            }
            _framework.RunOnFrameworkThread(() =>
            {
                _running = false;
                _tolk.SpeakInterrupt(message);
            });
        });
    }

    private string RunSearch(string query)
    {
        var sb          = new StringBuilder();
        var hits        = 0;
        var sheetCount  = 0;
        var failed      = new List<string>();

        sb.AppendLine($"=== Textsuche: '{query}' | Sprache {_data.Language} | {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");

        foreach (var name in _data.Excel.SheetNames)
        {
            if (hits >= MaxHits) break;
            try
            {
                // The variant decides which typed accessor Lumina accepts; asking
                // with the wrong one throws NotSupportedException (IDataManager docs).
                var isSubrow = _data.Excel.GetRawSheet(name) is RawSubrowExcelSheet;
                hits += isSubrow
                    ? SearchSubrowSheet(name, query, sb, MaxHits - hits)
                    : SearchRowSheet(name, query, sb, MaxHits - hits);
                sheetCount++;
            }
            catch (Exception ex)
            {
                // Some sheets exist only in other languages or not at all in this
                // client - listed in the file and the log, never silently dropped.
                failed.Add($"{name} ({ex.GetType().Name})");
            }
        }

        sb.AppendLine();
        sb.AppendLine($"=== {hits} Treffer in {sheetCount} Tabellen{(hits >= MaxHits ? $", nach {MaxHits} abgebrochen" : string.Empty)} ===");
        if (failed.Count > 0)
            sb.AppendLine($"=== {failed.Count} Tabellen nicht lesbar: {string.Join(", ", failed)} ===");

        _log.Info($"[TextSearch] {hits} Treffer in {sheetCount} Tabellen, {failed.Count} nicht lesbar.");

        var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "FFXIV_TextSearch.txt");
        try
        {
            File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            _log.Warning($"[TextSearch] Datei-Fehler: {ex.Message}");
            foreach (var line in sb.ToString().Split('\n'))
                if (line.TrimEnd('\r').Length > 0) _log.Info(line.TrimEnd('\r'));
            return AccessibilityStrings.TextSearchFileError(hits);
        }

        _log.Info($"[TextSearch] Gespeichert: {file}");
        return AccessibilityStrings.TextSearchDone(hits, sheetCount);
    }

    private int SearchRowSheet(string name, string query, StringBuilder sb, int budget)
    {
        var sheet   = _data.GetExcelSheet<RawRow>(name: name);
        var columns = StringColumns(sheet.Columns);
        if (columns.Count == 0) return 0;

        var hits = 0;
        foreach (var row in sheet)
        {
            if (hits >= budget) break;
            if (!RowMatches(row, columns, query)) continue;
            hits++;

            sb.AppendLine();
            sb.AppendLine($"--- {name} Zeile {row.RowId} ---");
            for (var id = (long)row.RowId - NeighbourRows; id <= row.RowId + NeighbourRows; id++)
            {
                if (id < 0 || !sheet.TryGetRow((uint)id, out var near)) continue;
                var marker = id == row.RowId ? ">>" : "  ";
                sb.AppendLine($"{marker} {id}: {RowText(near, columns)}");
            }
        }
        return hits;
    }

    private int SearchSubrowSheet(string name, string query, StringBuilder sb, int budget)
    {
        var sheet   = _data.GetSubrowExcelSheet<RawSubrow>(name: name);
        var columns = StringColumns(sheet.Columns);
        if (columns.Count == 0) return 0;

        var hits = 0;
        foreach (var subrows in sheet)
        {
            foreach (var row in subrows)
            {
                if (hits >= budget) return hits;
                if (!SubrowMatches(row, columns, query)) continue;
                hits++;
                sb.AppendLine();
                sb.AppendLine($"--- {name} Zeile {row.RowId}.{row.SubrowId} ---");
                sb.AppendLine($">> {SubrowText(row, columns)}");
            }
        }
        return hits;
    }

    private static List<int> StringColumns(IReadOnlyList<ExcelColumnDefinition> columns)
    {
        var result = new List<int>();
        for (var i = 0; i < columns.Count; i++)
            if (columns[i].Type == ExcelColumnDataType.String) result.Add(i);
        return result;
    }

    private static bool RowMatches(RawRow row, List<int> columns, string query)
    {
        foreach (var c in columns)
            if (row.ReadStringColumn(c).ExtractText().Contains(query, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static bool SubrowMatches(RawSubrow row, List<int> columns, string query)
    {
        foreach (var c in columns)
            if (row.ReadStringColumn(c).ExtractText().Contains(query, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static string RowText(RawRow row, List<int> columns)
    {
        var parts = new List<string>();
        foreach (var c in columns)
        {
            var text = row.ReadStringColumn(c).ExtractText();
            if (text.Length > 0) parts.Add($"[{c}] {text.Replace("\n", " / ")}");
        }
        return parts.Count > 0 ? string.Join(" | ", parts) : "(leer)";
    }

    private static string SubrowText(RawSubrow row, List<int> columns)
    {
        var parts = new List<string>();
        foreach (var c in columns)
        {
            var text = row.ReadStringColumn(c).ExtractText();
            if (text.Length > 0) parts.Add($"[{c}] {text.Replace("\n", " / ")}");
        }
        return parts.Count > 0 ? string.Join(" | ", parts) : "(leer)";
    }
}
