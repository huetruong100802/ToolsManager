using System.Linq;
using Toolbox.Core;
using Toolbox.Core.Workspaces;
using Xunit;

namespace Toolbox.Tests
{
    /// <summary>Script npm phải ra <c>npm run x</c>, kể cả khi tool npm trong thư viện được tạo tay và thiếu nguồn hậu tố.</summary>
    public class CommandBuilderNpmTests
    {
        [Fact]
        public void HandMadeNpmToolFallsBackToPresetRunTemplate()
        {
            var def = ToolDef.From(new ToolEntry { Id = "npm", Name = "nodejs", Path = @"C:\nvm\v24", Command = "npm", Pack = PackMode.None });

            Assert.NotNull(def.ActionsFrom);
            Assert.Equal("npm run {id}", def.ActionsFrom.Template);
        }

        [Fact]
        public void ToolWithOwnSuffixSourceKeepsIt()
        {
            var source = new SuffixSource { File = "package.json", JsonPath = "scripts", Template = "yarn {id}" };
            var def = ToolDef.From(new ToolEntry { Id = "npm", Name = "npm", Command = "npm", Pack = PackMode.None, SuffixSource = source });

            Assert.Equal("yarn {id}", def.ActionsFrom.Template);
        }

        [Fact]
        public void ToolWithDifferentCommandIsNotBackfilled()
        {
            var def = ToolDef.From(new ToolEntry { Id = "npm", Name = "npm", Command = "pnpm", Pack = PackMode.None });

            Assert.Null(def.ActionsFrom);
        }

        [Fact]
        public void UnrelatedToolIsUntouched()
        {
            var def = ToolDef.From(new ToolEntry { Id = "git", Name = "git", Command = "git", Pack = PackMode.None });

            Assert.Null(def.ActionsFrom);
        }
    }
}
