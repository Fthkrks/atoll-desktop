using System;
using System.IO;
using HyTranslate.Core.Documents;
using Xunit;

namespace HyTranslate.Core.Tests;

public class MakeUniquePathTests
{
    [Fact]
    public void MakeUniquePath_FileDoesNotExist_ReturnsSamePath()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"non_existent_{Guid.NewGuid()}.tr.pdf");
        var result = DocumentTranslatorFactory.MakeUniquePath(tempFile);
        Assert.Equal(tempFile, result);
    }

    [Fact]
    public void MakeUniquePath_FileExists_GeneratesIndexedName()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"unique_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(dir);

        try
        {
            var baseFile = Path.Combine(dir, "document.tr.pdf");
            File.WriteAllText(baseFile, "content");

            var unique1 = DocumentTranslatorFactory.MakeUniquePath(baseFile);
            var expected1 = Path.Combine(dir, "document.tr (2).pdf");
            Assert.Equal(expected1, unique1);

            // Create document.tr (2).pdf
            File.WriteAllText(expected1, "content 2");

            var unique2 = DocumentTranslatorFactory.MakeUniquePath(baseFile);
            var expected2 = Path.Combine(dir, "document.tr (3).pdf");
            Assert.Equal(expected2, unique2);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
