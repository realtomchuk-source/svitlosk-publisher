using System;
using System.Text;
using SvitloSk.Publisher.Application.Orchestration;
using Xunit;

namespace SvitloSk.Publisher.Tests.Unit;

public class ContentHashCalculatorTests
{
    private readonly ContentHashCalculator _calculator = new();

    [Fact]
    public void ComputeHash_WithSameInputs_ShouldProduceIdenticalHashes()
    {
        var text = "Привіт, Світловодськ!";
        var hash1 = _calculator.ComputeHash(text, null);
        var hash2 = _calculator.ComputeHash(text, null);

        Assert.Equal(hash1, hash2);
        Assert.Equal(64, hash1.Length);
    }

    [Fact]
    public void ComputeHash_WithDifferentLineEndings_ShouldProduceIdenticalHashes()
    {
        var textLf = "Line 1\nLine 2\nLine 3";
        var textCrlf = "Line 1\r\nLine 2\r\nLine 3";
        var textCr = "Line 1\rLine 2\rLine 3";

        var hashLf = _calculator.ComputeHash(textLf, null);
        var hashCrlf = _calculator.ComputeHash(textCrlf, null);
        var hashCr = _calculator.ComputeHash(textCr, null);

        Assert.Equal(hashLf, hashCrlf);
        Assert.Equal(hashLf, hashCr);
    }

    [Fact]
    public void ComputeHash_WithDifferentUnicodeNormalization_ShouldProduceIdenticalHashes()
    {
        // "і" in NFD vs NFC representation
        // NFC: U+0456 (CYRILLIC SMALL LETTER BYELORUSSIAN-UKRAINIAN I)
        // NFD: Cyrillic small letter i is already NFD stable, but let's use "й" (Cyrillic Small Letter Short I)
        // NFC: "й" (U+0439)
        // NFD: "и" (U+0438) + Combining Breve (U+0306)
        
        string nfcText = "й";
        string nfdText = "\u0438\u0306"; 

        var hashNfc = _calculator.ComputeHash(nfcText, null);
        var hashNfd = _calculator.ComputeHash(nfdText, null);

        Assert.Equal(hashNfc, hashNfd);
    }

    [Fact]
    public void ComputeHash_WithTrailingWhitespaces_ShouldProduceIdenticalHashes()
    {
        var cleanText = "Line 1\nLine 2";
        var spacesText = "Line 1  \t\nLine 2 \t";

        var hashClean = _calculator.ComputeHash(cleanText, null);
        var hashSpaces = _calculator.ComputeHash(spacesText, null);

        Assert.Equal(hashClean, hashSpaces);
    }

    [Fact]
    public void ComputeHash_WithInternalSpacesChanged_ShouldProduceDifferentHashes()
    {
        var text1 = "Line 1 Line 2";
        var text2 = "Line 1  Line 2";

        var hash1 = _calculator.ComputeHash(text1, null);
        var hash2 = _calculator.ComputeHash(text2, null);

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void ComputeHash_WithChangedGraphicBytes_ShouldProduceDifferentHashes()
    {
        var text = "Image test";
        byte[] image1 = { 1, 2, 3 };
        byte[] image2 = { 1, 2, 4 };

        var hash1 = _calculator.ComputeHash(text, image1);
        var hash2 = _calculator.ComputeHash(text, image2);
        var hashTextOnly = _calculator.ComputeHash(text, null);

        Assert.NotEqual(hash1, hash2);
        Assert.NotEqual(hash1, hashTextOnly);
    }

    [Fact]
    public void ComputeHash_NullAndEmptyGraphicBytes_ShouldProduceIdenticalHashes()
    {
        var text = "Empty image test";
        var hashNull = _calculator.ComputeHash(text, null);
        var hashEmptyArray = _calculator.ComputeHash(text, Array.Empty<byte>());

        Assert.Equal(hashNull, hashEmptyArray);
    }

    [Fact]
    public void ComputeHash_WithDifferentCollectionOrdering_ProducesSameHash()
    {
        var inputA = new[] { "вулиця Б", "вулиця А", "вулиця В" };
        var inputB = new[] { "вулиця В", "вулиця Б", "вулиця А" };

        var sortedA = CollectionSorter.SortCollections(inputA);
        var sortedB = CollectionSorter.SortCollections(inputB);

        var textA = string.Join(", ", sortedA);
        var textB = string.Join(", ", sortedB);

        var hashA = _calculator.ComputeHash(textA, null);
        var hashB = _calculator.ComputeHash(textB, null);

        Assert.Equal(hashA, hashB);
        Assert.Equal("вулиця А", sortedA[0]);
        Assert.Equal("вулиця Б", sortedA[1]);
        Assert.Equal("вулиця В", sortedA[2]);
    }
}
