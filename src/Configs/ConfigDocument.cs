using System;
using System.Collections.Generic;
using System.Linq;

namespace QM_CompanyTechTiers.Configs
{
    /// <summary>
    /// One row of a config table. Cells are the tab-split contents with the trailing
    /// carriage return stripped; <see cref="HadCarriageReturn"/> remembers whether to put it back.
    /// </summary>
    public sealed class ConfigRow
    {
        internal string[] Cells;
        internal readonly bool HadCarriageReturn;
        internal bool Dirty;

        internal ConfigRow(string[] cells, bool hadCarriageReturn)
        {
            Cells = cells;
            HadCarriageReturn = hadCarriageReturn;
        }

        public string Get(int index) =>
            index >= 0 && index < Cells.Length ? Cells[index] : string.Empty;

        public bool IsBlank => Cells.All(c => c.Length == 0);

        internal string Render() =>
            string.Join("\t", Cells) + (HadCarriageReturn ? "\r" : string.Empty);
    }

    /// <summary>A <c>#name ... #end</c> block: a header row of column names followed by data rows.</summary>
    public sealed class ConfigSection
    {
        private readonly List<ConfigRow> _rows = new List<ConfigRow>();
        private readonly List<string> _columns;

        public string Name { get; }
        public IReadOnlyList<string> Columns => _columns;
        public IReadOnlyList<ConfigRow> Rows => _rows;

        /// <summary>Index of the document line holding the column header. Rows follow it.</summary>
        internal int HeaderLineIndex;
        internal readonly List<int> RowLineIndexes = new List<int>();

        internal ConfigSection(string name, List<string> columns, int headerLineIndex)
        {
            Name = name;
            _columns = columns;
            HeaderLineIndex = headerLineIndex;
        }

        public int ColumnIndex(string column)
        {
            for (int i = 0; i < _columns.Count; i++)
                if (string.Equals(_columns[i], column, StringComparison.Ordinal))
                    return i;
            return -1;
        }

        internal void AddRow(ConfigRow row, int lineIndex)
        {
            _rows.Add(row);
            RowLineIndexes.Add(lineIndex);
        }
    }

    /// <summary>
    /// Quasimorph's tab-separated config format. Parsing mirrors <c>MGSC.ConfigLoader.LoadSpecificFile</c>:
    /// blank lines and <c>//</c> comments are ignored, a line containing <c>#end</c> closes a section,
    /// any other line starting with <c>#</c> opens one, and the first line after that is the header.
    /// </summary>
    public sealed class ConfigDocument
    {
        private readonly List<string> _lines;
        private readonly List<ConfigSection> _sections;

        private ConfigDocument(List<string> lines, List<ConfigSection> sections)
        {
            _lines = lines;
            _sections = sections;
        }

        public IReadOnlyList<ConfigSection> Sections => _sections;

        public ConfigSection Section(string name)
        {
            foreach (var s in _sections)
                if (string.Equals(s.Name, name, StringComparison.Ordinal))
                    return s;
            return null;
        }

        public static ConfigDocument Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));

            var lines = text.Split('\n').ToList();
            var sections = new List<ConfigSection>();

            ConfigSection current = null;
            bool expectHeader = false;

            for (int i = 0; i < lines.Count; i++)
            {
                string raw = lines[i];
                string body = raw.TrimEnd('\r');

                if (body.Length == 0 || body.TrimStart().StartsWith("//", StringComparison.Ordinal))
                    continue;

                if (body.Contains("#end"))
                {
                    current = null;
                    expectHeader = false;
                    continue;
                }

                if (body[0] == '#')
                {
                    string name = body.Trim('\t', '\r', '\n', '#');
                    current = new ConfigSection(name, body.Split('\t').ToList(), i);
                    sections.Add(current);
                    expectHeader = true;
                    continue;
                }

                if (current == null) continue;

                if (expectHeader)
                {
                    expectHeader = false;
                    var columns = body.Split('\t').ToList();
                    var replacement = new ConfigSection(current.Name, columns, i);
                    sections[sections.Count - 1] = replacement;
                    current = replacement;
                    continue;
                }

                current.AddRow(new ConfigRow(body.Split('\t'), raw.EndsWith("\r", StringComparison.Ordinal)), i);
            }

            return new ConfigDocument(lines, sections);
        }

        public string Render()
        {
            foreach (var section in _sections)
                for (int r = 0; r < section.Rows.Count; r++)
                    if (((ConfigRow)section.Rows[r]).Dirty)
                        _lines[section.RowLineIndexes[r]] = ((ConfigRow)section.Rows[r]).Render();

            return string.Join("\n", _lines);
        }
    }
}
