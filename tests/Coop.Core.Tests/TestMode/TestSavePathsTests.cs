using System;
using Coop.Core.TestMode;
using Xunit;

namespace Coop.Core.Tests.TestMode
{
    public class TestSavePathsTests
    {
        [Fact]
        public void Resolves_into_the_test_client_folder()
        {
            Assert.Equal(@"C:\p\CoopTestClient\slot1.sav", TestSavePaths.Resolve(@"C:\p", "slot1.sav"));
        }

        [Fact]
        public void Resolves_nested_relative_files()
        {
            Assert.Equal(@"C:\p\CoopTestClient\sub\a.dat", TestSavePaths.Resolve(@"C:\p", @"sub\a.dat"));
        }

        [Fact]
        public void Parent_segments_are_refused()
        {
            Assert.Throws<ArgumentException>(() => TestSavePaths.Resolve(@"C:\p", @"..\x"));
            Assert.Throws<ArgumentException>(() => TestSavePaths.Resolve(@"C:\p", "a/../../x"));
        }

        [Fact]
        public void Rooted_paths_are_refused()
        {
            Assert.Throws<ArgumentException>(() => TestSavePaths.Resolve(@"C:\p", @"C:\x"));
            Assert.Throws<ArgumentException>(() => TestSavePaths.Resolve(@"C:\p", @"\x"));
        }

        [Fact]
        public void Empty_file_name_is_refused()
        {
            Assert.Throws<ArgumentException>(() => TestSavePaths.Resolve(@"C:\p", ""));
        }
    }
}
