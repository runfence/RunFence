using System.Security.AccessControl;
using RunFence.Acl;
using RunFence.Core.Models;
using Xunit;

namespace RunFence.Tests;

/// <summary>
/// Tests for <see cref="GrantRightsMapper"/> covering all mapping methods and constants.
/// Pure in-memory logic — no I/O.
/// </summary>
public class GrantRightsMapperTests
{
    // --- MapAllowRights ---

    [Fact]
    public void MapAllowRights_ReadAlwaysIncluded_EvenWhenFlagOff()
    {
        // Read is always-on in allow mode regardless of the Read flag value
        var rights = new SavedRightsState(Execute: false, Write: false, Read: false, Special: false, Own: false);

        var result = GrantRightsMapper.MapAllowRights(rights, isFolder: true);

        Assert.Equal(GrantRightsMapper.ReadMask, result & GrantRightsMapper.ReadMask);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MapAllowRights_ExecuteOnlyWhenEnabled(bool execute)
    {
        var rights = new SavedRightsState(Execute: execute, Write: false, Read: true, Special: false, Own: false);

        var result = GrantRightsMapper.MapAllowRights(rights, isFolder: true);

        Assert.Equal(execute, (result & GrantRightsMapper.ExecuteMask) == GrantRightsMapper.ExecuteMask);
    }

    [Fact]
    public void MapAllowRights_WriteFolderIncludesDeleteSubdirectoriesAndFiles()
    {
        var rights = new SavedRightsState(Execute: false, Write: true, Read: true, Special: false, Own: false);

        var result = GrantRightsMapper.MapAllowRights(rights, isFolder: true);

        Assert.True((result & FileSystemRights.DeleteSubdirectoriesAndFiles) != 0);
        Assert.False((result & FileSystemRights.Delete) != 0);
    }

    [Fact]
    public void MapAllowRights_WriteFileIncludesDeleteNotDeleteSubdirectoriesAndFiles()
    {
        var rights = new SavedRightsState(Execute: false, Write: true, Read: true, Special: false, Own: false);

        var result = GrantRightsMapper.MapAllowRights(rights, isFolder: false);

        Assert.True((result & FileSystemRights.Delete) != 0);
        Assert.False((result & FileSystemRights.DeleteSubdirectoriesAndFiles) != 0);
    }

    [Fact]
    public void MapAllowRights_SpecialFolderIncludesDelete()
    {
        var rights = new SavedRightsState(Execute: false, Write: false, Read: true, Special: true, Own: false);

        var result = GrantRightsMapper.MapAllowRights(rights, isFolder: true);

        Assert.True((result & FileSystemRights.Delete) != 0,
            "SpecialFolderMask should include Delete");
    }

    [Fact]
    public void MapAllowRights_SpecialFileDoesNotIncludeDelete()
    {
        var rights = new SavedRightsState(Execute: false, Write: false, Read: true, Special: true, Own: false);

        var result = GrantRightsMapper.MapAllowRights(rights, isFolder: false);

        Assert.False((result & FileSystemRights.Delete) != 0,
            "SpecialFileMask should not include Delete");
    }

    [Fact]
    public void MapAllowRights_AllFlags_FolderProducesExpectedMask()
    {
        var rights = new SavedRightsState(Execute: true, Write: true, Read: true, Special: true, Own: false);
        var expected = GrantRightsMapper.ReadMask | GrantRightsMapper.ExecuteMask |
                       GrantRightsMapper.WriteFolderMask | GrantRightsMapper.SpecialFolderMask;

        var result = GrantRightsMapper.MapAllowRights(rights, isFolder: true);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void MapAllowRights_AllFlags_FileProducesExpectedMask()
    {
        var rights = new SavedRightsState(Execute: true, Write: true, Read: true, Special: true, Own: false);
        var expected = GrantRightsMapper.ReadMask | GrantRightsMapper.ExecuteMask |
                       GrantRightsMapper.WriteFileMask | GrantRightsMapper.SpecialFileMask;

        var result = GrantRightsMapper.MapAllowRights(rights, isFolder: false);

        Assert.Equal(expected, result);
    }

    // --- MapDenyRights ---

    [Fact]
    public void MapDenyRights_WriteAndSpecialAlwaysIncluded_EvenWhenFlagsOff()
    {
        // Write+Special are always-on in deny mode regardless of flag values
        var rights = new SavedRightsState(Execute: false, Write: false, Read: false, Special: false, Own: false);

        var result = GrantRightsMapper.MapDenyRights(rights, isFolder: true);

        Assert.Equal(GrantRightsMapper.WriteFolderMask, result & GrantRightsMapper.WriteFolderMask);
        Assert.Equal(GrantRightsMapper.SpecialFolderMask, result & GrantRightsMapper.SpecialFolderMask);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MapDenyRights_ReadIncludedOnlyWhenEnabled(bool read)
    {
        var rights = new SavedRightsState(Execute: false, Write: true, Read: read, Special: true, Own: false);

        var result = GrantRightsMapper.MapDenyRights(rights, isFolder: true);

        Assert.Equal(read, (result & GrantRightsMapper.ReadMask) == GrantRightsMapper.ReadMask);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MapDenyRights_ExecuteIncludedOnlyWhenEnabled(bool execute)
    {
        var rights = new SavedRightsState(Execute: execute, Write: true, Read: false, Special: true, Own: false);

        var result = GrantRightsMapper.MapDenyRights(rights, isFolder: true);

        Assert.Equal(execute, (result & GrantRightsMapper.ExecuteMask) == GrantRightsMapper.ExecuteMask);
    }

    [Fact]
    public void MapDenyRights_AllFlags_FileProducesExpectedMask()
    {
        var rights = new SavedRightsState(Execute: true, Write: true, Read: true, Special: true, Own: false);
        var expected = GrantRightsMapper.ReadMask | GrantRightsMapper.ExecuteMask |
                       GrantRightsMapper.WriteFileMask | GrantRightsMapper.SpecialFileMask;

        var result = GrantRightsMapper.MapDenyRights(rights, isFolder: false);

        Assert.Equal(expected, result);
    }

    // --- FromNtfsRights (reverse mapping) ---

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FromNtfsRights_AllowReadOnly_ReturnsDefaultAllowRights(bool isFolder)
    {
        var result = GrantRightsMapper.FromNtfsRights(
            GrantRightsMapper.ReadMask, denyRights: 0, isDeny: false, isFolder, RightCheckState.Unchecked, false);

        Assert.True(result.Read);
        Assert.False(result.Execute);
        Assert.False(result.Write);
        Assert.False(result.Special);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FromNtfsRights_AllowWithExecute_ReturnsExecuteTrue(bool isFolder)
    {
        var allow = GrantRightsMapper.ReadMask | GrantRightsMapper.ExecuteMask;

        var result = GrantRightsMapper.FromNtfsRights(allow, 0, isDeny: false, isFolder, RightCheckState.Unchecked, false);

        Assert.True(result.Execute);
    }

    [Fact]
    public void FromNtfsRights_AllowWithWriteFolder_ReturnsWriteTrue()
    {
        var allow = GrantRightsMapper.ReadMask | GrantRightsMapper.WriteFolderMask;

        var result = GrantRightsMapper.FromNtfsRights(allow, 0, isDeny: false, isFolder: true, RightCheckState.Unchecked, false);

        Assert.True(result.Write);
    }

    [Fact]
    public void FromNtfsRights_AllowWithWriteFile_ReturnsWriteTrue()
    {
        var allow = GrantRightsMapper.ReadMask | GrantRightsMapper.WriteFileMask;

        var result = GrantRightsMapper.FromNtfsRights(allow, 0, isDeny: false, isFolder: false, RightCheckState.Unchecked, false);

        Assert.True(result.Write);
    }

    [Fact]
    public void FromNtfsRights_DenyWithWriteAndSpecial_ReturnsDenyMode()
    {
        var deny = GrantRightsMapper.WriteFolderMask | GrantRightsMapper.SpecialFolderMask;

        var result = GrantRightsMapper.FromNtfsRights(0, deny, isDeny: true, isFolder: true, RightCheckState.Unchecked, false);

        Assert.True(result.Write);
        Assert.True(result.Special);
        Assert.False(result.Read);
        Assert.False(result.Execute);
    }

    [Fact]
    public void FromNtfsRights_DenyWithReadAndExecute_ReturnsReadAndExecuteTrue()
    {
        var deny = GrantRightsMapper.WriteFolderMask | GrantRightsMapper.SpecialFolderMask |
                   GrantRightsMapper.ReadMask | GrantRightsMapper.ExecuteMask;

        var result = GrantRightsMapper.FromNtfsRights(0, deny, isDeny: true, isFolder: true, RightCheckState.Unchecked, false);

        Assert.True(result.Read);
        Assert.True(result.Execute);
    }

    [Fact]
    public void FromNtfsRights_DenyReadOnly_ReturnsDenyModeCorrectly()
    {
        // Deny with only Read — no write mask present. isDeny=true from caller, not inferred.
        var deny = GrantRightsMapper.ReadMask;

        var result = GrantRightsMapper.FromNtfsRights(0, deny, isDeny: true, isFolder: true, RightCheckState.Unchecked, false);

        Assert.True(result.Read);
        Assert.True(result.Write);   // always on in deny mode
        Assert.True(result.Special); // always on in deny mode
    }

    [Fact]
    public void FromNtfsRights_AccountOwner_OwnTrue()
    {
        var result = GrantRightsMapper.FromNtfsRights(
            GrantRightsMapper.ReadMask, 0, isDeny: false, isFolder: true, RightCheckState.Checked, false);

        Assert.True(result.Own);
    }

    [Fact]
    public void FromNtfsRights_AdminOwner_OwnTrueInDenyMode()
    {
        var deny = GrantRightsMapper.WriteFolderMask | GrantRightsMapper.SpecialFolderMask;

        var result = GrantRightsMapper.FromNtfsRights(0, deny, isDeny: true, isFolder: true, RightCheckState.Unchecked, isAdminOwner: true);

        Assert.True(result.Own);
    }

    // --- IsTraverseOnly ---

    [Fact]
    public void IsTraverseOnly_ExactTraverseMask_ReturnsTrue()
    {
        Assert.True(GrantRightsMapper.IsTraverseOnly(GrantRightsMapper.TraverseOnlyMask));
    }

    [Fact]
    public void IsTraverseOnly_TraversePlusRead_ReturnsFalse()
    {
        var rights = GrantRightsMapper.TraverseOnlyMask | GrantRightsMapper.ReadMask;

        Assert.False(GrantRightsMapper.IsTraverseOnly(rights));
    }

    [Fact]
    public void IsTraverseOnly_ZeroRights_ReturnsFalse()
    {
        Assert.False(GrantRightsMapper.IsTraverseOnly(0));
    }

    [Fact]
    public void IsTraverseOnly_ReadOnlyRights_ReturnsFalse()
    {
        Assert.False(GrantRightsMapper.IsTraverseOnly(GrantRightsMapper.ReadMask));
    }

    // --- FromRights ---

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FromRights_AllowMode_ReadAlwaysTrueOwnAlwaysFalse(bool isFolder)
    {
        var result = GrantRightsMapper.FromRights(GrantRightsMapper.ReadMask, isFolder, isDeny: false);

        Assert.True(result.Read);
        Assert.False(result.Own);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FromRights_AllowMode_ExecuteSetWhenMaskPresent(bool isFolder)
    {
        var rights = GrantRightsMapper.ReadMask | GrantRightsMapper.ExecuteMask;

        var result = GrantRightsMapper.FromRights(rights, isFolder, isDeny: false);

        Assert.True(result.Execute);
    }

    [Fact]
    public void FromRights_AllowMode_FolderWriteSetWhenFolderWriteMaskPresent()
    {
        var rights = GrantRightsMapper.ReadMask | GrantRightsMapper.WriteFolderMask;

        var result = GrantRightsMapper.FromRights(rights, isFolder: true, isDeny: false);

        Assert.True(result.Write);
    }

    [Fact]
    public void FromRights_AllowMode_FileWriteSetWhenFileWriteMaskPresent()
    {
        var rights = GrantRightsMapper.ReadMask | GrantRightsMapper.WriteFileMask;

        var result = GrantRightsMapper.FromRights(rights, isFolder: false, isDeny: false);

        Assert.True(result.Write);
    }

    [Fact]
    public void FromRights_AllowMode_FolderWriteFalseWhenFileWriteMaskUsed()
    {
        // WriteFileMask does not fully cover WriteFolderMask (missing DeleteSubdirectoriesAndFiles)
        var result = GrantRightsMapper.FromRights(
            GrantRightsMapper.ReadMask | GrantRightsMapper.WriteFileMask, isFolder: true, isDeny: false);

        Assert.False(result.Write);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FromRights_AllowMode_AllFlagsProduceFullRights(bool isFolder)
    {
        var writeMask = isFolder ? GrantRightsMapper.WriteFolderMask : GrantRightsMapper.WriteFileMask;
        var specialMask = isFolder ? GrantRightsMapper.SpecialFolderMask : GrantRightsMapper.SpecialFileMask;
        var rights = GrantRightsMapper.ReadMask | GrantRightsMapper.ExecuteMask | writeMask | specialMask;

        var result = GrantRightsMapper.FromRights(rights, isFolder, isDeny: false);

        Assert.True(result.Read);
        Assert.True(result.Execute);
        Assert.True(result.Write);
        Assert.True(result.Special);
        Assert.False(result.Own);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FromRights_DenyMode_WriteAndSpecialAlwaysTrue(bool isFolder)
    {
        // Even with zero rights, deny mode always returns Write=true, Special=true
        var result = GrantRightsMapper.FromRights(0, isFolder, isDeny: true);

        Assert.True(result.Write);
        Assert.True(result.Special);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FromRights_DenyMode_ReadAndExecuteReflectMask(bool isFolder)
    {
        var rights = GrantRightsMapper.ReadMask | GrantRightsMapper.ExecuteMask;

        var result = GrantRightsMapper.FromRights(rights, isFolder, isDeny: true);

        Assert.True(result.Read);
        Assert.True(result.Execute);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FromRights_DenyMode_OwnAlwaysFalse(bool isFolder)
    {
        var result = GrantRightsMapper.FromRights(
            GrantRightsMapper.ReadMask | GrantRightsMapper.ExecuteMask, isFolder, isDeny: true);

        Assert.False(result.Own);
    }

    // --- Constants consistency ---

    [Fact]
    public void TraverseOnlyMask_MatchesTraverseRightsHelper()
    {
        // TraverseRightsHelper.TraverseRights uses Traverse (= ExecuteFile) | ReadAttributes | Synchronize
        // GrantRightsMapper.TraverseOnlyMask must match exactly
        var expected = FileSystemRights.ExecuteFile | FileSystemRights.ReadAttributes | FileSystemRights.Synchronize;

        Assert.Equal(expected, GrantRightsMapper.TraverseOnlyMask);
    }

    [Fact]
    public void WriteFolderMask_AndWriteFileMask_DifferOnlyByDeleteFlag()
    {
        var folderOnly = GrantRightsMapper.WriteFolderMask & ~GrantRightsMapper.WriteFileMask;
        var fileOnly = GrantRightsMapper.WriteFileMask & ~GrantRightsMapper.WriteFolderMask;

        Assert.Equal(FileSystemRights.DeleteSubdirectoriesAndFiles, folderOnly);
        Assert.Equal(FileSystemRights.Delete, fileOnly);
    }

    [Fact]
    public void SpecialFolderMask_AndSpecialFileMask_DifferOnlyByDeleteFlag()
    {
        var folderOnly = GrantRightsMapper.SpecialFolderMask & ~GrantRightsMapper.SpecialFileMask;
        var fileOnly = GrantRightsMapper.SpecialFileMask & ~GrantRightsMapper.SpecialFolderMask;

        Assert.Equal(FileSystemRights.Delete, folderOnly);
        Assert.Equal((FileSystemRights)0, fileOnly);
    }
}
