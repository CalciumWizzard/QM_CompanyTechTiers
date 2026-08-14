using System.Linq;
using QM_CompanyTechTiers.Configs;
using Xunit;

namespace QM_CompanyTechTiers.Tests
{
    public class ConfigDocumentTests
    {
        [Fact]
        public void Parses_sections_headers_and_rows()
        {
            string text = "#alpha\t\t\r\nId\tValue\t\r\na\t1\t\r\n#end\t\t\r\n#beta\t\r\nId\t\r\nb\t\r\n#end\t";
            var doc = ConfigDocument.Parse(text);

            Assert.Equal(new[] { "alpha", "beta" }, doc.Sections.Select(s => s.Name).ToArray());
            Assert.Equal(new[] { "Id", "Value", "" }, doc.Section("alpha").Columns.ToArray());
            Assert.Single(doc.Section("alpha").Rows);
            Assert.Equal("a", doc.Section("alpha").Rows[0].Get(0));
            Assert.Equal("1", doc.Section("alpha").Rows[0].Get(1));
            Assert.Equal(1, doc.Section("alpha").ColumnIndex("Value"));
            Assert.Equal(-1, doc.Section("alpha").ColumnIndex("Nope"));
            Assert.Null(doc.Section("missing"));
        }

        [Fact]
        public void Get_past_end_of_row_returns_empty_string()
        {
            var doc = ConfigDocument.Parse("#s\r\nId\tValue\r\nonly\r\n#end");
            Assert.Equal("", doc.Section("s").Rows[0].Get(1));
            Assert.Equal("", doc.Section("s").Rows[0].Get(99));
        }

        [Fact]
        public void Blank_lines_between_sections_survive_and_are_not_rows()
        {
            var doc = ConfigDocument.Parse("#a\r\nId\r\nx\r\n#end\r\n\r\n#b\r\nId\r\ny\r\n#end");
            Assert.Single(doc.Section("a").Rows);
            Assert.Single(doc.Section("b").Rows);
        }

        [FixtureFact]
        public void Round_trips_every_real_config_byte_for_byte()
        {
            foreach (string text in new[] { Fixtures.Items, Fixtures.FactionDrops, Fixtures.Crafting })
                Assert.Equal(text, ConfigDocument.Parse(text).Render());
        }

        [FixtureFact]
        public void Finds_the_known_sections_in_the_real_configs()
        {
            Assert.NotNull(ConfigDocument.Parse(Fixtures.Items).Section("datadisks"));
            Assert.NotNull(ConfigDocument.Parse(Fixtures.Crafting).Section("itemreceipts"));

            var drops = ConfigDocument.Parse(Fixtures.FactionDrops);
            Assert.Contains(drops.Sections, s => s.Name == "factiondrop_AnCom_rewardChips");
        }

        [Fact]
        public void Set_changes_a_cell_and_render_reflects_it()
        {
            var doc = ConfigDocument.Parse("#s\r\nId\tValue\r\na\t1\r\n#end");
            doc.Section("s").Rows[0].Set(1, "42");
            Assert.Equal("#s\r\nId\tValue\r\na\t42\r\n#end", doc.Render());
        }

        [Fact]
        public void Set_past_end_widens_the_row()
        {
            var doc = ConfigDocument.Parse("#s\r\nId\tA\tB\r\nx\r\n#end");
            doc.Section("s").Rows[0].Set(2, "z");
            Assert.Equal("#s\r\nId\tA\tB\r\nx\t\tz\r\n#end", doc.Render());
        }

        [Fact]
        public void InsertRowAfter_places_the_row_directly_below_and_pads_to_column_count()
        {
            var doc = ConfigDocument.Parse("#s\r\nId\tA\tB\r\nfirst\t1\t2\r\nsecond\t3\t4\r\n#end");
            var section = doc.Section("s");
            section.InsertRowAfter(section.Rows[0], new[] { "new", "9" });

            Assert.Equal("#s\r\nId\tA\tB\r\nfirst\t1\t2\r\nnew\t9\t\r\nsecond\t3\t4\r\n#end", doc.Render());
            Assert.Equal(3, section.Rows.Count);
            Assert.Equal("new", section.Rows[1].Get(0));
        }

        [Fact]
        public void InsertRowAfter_keeps_later_sections_intact()
        {
            var doc = ConfigDocument.Parse("#a\r\nId\r\nx\r\n#end\r\n#b\r\nId\r\ny\r\n#end");
            var a = doc.Section("a");
            a.InsertRowAfter(a.Rows[0], new[] { "inserted" });

            Assert.Equal("#a\r\nId\r\nx\r\ninserted\r\n#end\r\n#b\r\nId\r\ny\r\n#end", doc.Render());
            Assert.Single(doc.Section("b").Rows);
            Assert.Equal("y", doc.Section("b").Rows[0].Get(0));
        }

        [FixtureFact]
        public void Inserting_into_a_real_config_changes_only_the_expected_line_count()
        {
            var doc = ConfigDocument.Parse(Fixtures.FactionDrops);
            var section = doc.Section("factiondrop_AnCom_rewardChips");
            int before = Fixtures.FactionDrops.Split('\n').Length;

            section.InsertRowAfter(section.Rows[0], new[] { "3", "test_chip", "10", "300" });

            Assert.Equal(before + 1, doc.Render().Split('\n').Length);
            Assert.Equal(section.Rows.Count, ConfigDocument.Parse(doc.Render())
                .Section("factiondrop_AnCom_rewardChips").Rows.Count);
        }
    }
}
