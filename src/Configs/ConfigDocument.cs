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

        /// <summary>Set by <c>Set(int, string)</c>, added in Task 2, when a cell is edited.</summary>
        internal bool Dirty;

        internal ConfigRow(string[] cells, bool hadCarriageReturn)
        {
            Cells = cells;
            HadCarriageReturn = hadCarriageReturn;
        }

        public string Get(int index) =>
            index >= 0 && index < Cells.Length ? Cells[index] : string.Empty;

        public void Set(int index, string value)
        {
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
            if (index >= Cells.Length)
            {
                var widened = new string[index + 1];
                Array.Copy(Cells, widened, Cells.Length);
                for (int i = Cells.Length; i < widened.Length; i++) widened[i] = string.Empty;
                Cells = widened;
            }
            Cells[index] = value;
            Dirty = true;
        }

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

        internal ConfigDocument Owner;

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

        public ConfigRow InsertRowAfter(ConfigRow existing, IEnumerable<string> cells)
        {
            int position = _rows.IndexOf(existing);
            if (position < 0) throw new ArgumentException("Row does not belong to this section.", nameof(existing));

            var padded = cells.ToList();
            while (padded.Count < _columns.Count) padded.Add(string.Empty);

            var row = new ConfigRow(padded.ToArray(), hadCarriageReturn: true);
            int lineIndex = RowLineIndexes[position] + 1;

            Owner.InsertLine(lineIndex, row.Render());
            _rows.Insert(position + 1, row);
            RowLineIndexes.Insert(position + 1, lineIndex);
            return row;
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

            var document = new ConfigDocument(lines, sections);
            foreach (var section in sections) section.Owner = document;
            return document;
        }

        internal void InsertLine(int lineIndex, string content)
        {
            _lines.Insert(lineIndex, content);
            foreach (var section in _sections)
            {
                if (section.HeaderLineIndex >= lineIndex) section.HeaderLineIndex++;
                for (int i = 0; i < section.RowLineIndexes.Count; i++)
                    if (section.RowLineIndexes[i] >= lineIndex)
                        section.RowLineIndexes[i]++;
            }
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
