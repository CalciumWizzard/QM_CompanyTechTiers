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
            Assert.Equal(1, doc.Section("alpha").Rows.Count);
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
            Assert.Equal(1, doc.Section("a").Rows.Count);
            Assert.Equal(1, doc.Section("b").Rows.Count);
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
    }
}
