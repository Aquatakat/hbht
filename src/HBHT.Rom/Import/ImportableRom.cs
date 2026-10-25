namespace HBHT.Rom.Import;

/// <summary>
/// The ROM a player picks to import, checked before the import starts, with what's wrong worded for them: the editor's import
/// page asks for a North American 1.0 ROM, the primary target (docs/formats/rom.md), in any byte order <see cref="RomImage"/>
/// reads (<c>.z64</c>, <c>.v64</c>, <c>.n64</c>). The CLI's <c>hbht import</c> also takes 1.1.
/// </summary>
public static class ImportableRom
{
    /// <summary>The ROM at <paramref name="path"/>, if it's NA 1.0.</summary>
    /// <exception cref="InvalidDataException">It isn't, with the reason worded for the player.</exception>
    /// <exception cref="IOException">It can't be read.</exception>
    public static RomImage Open(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidDataException("Choose a ROM to import.");
        if (!File.Exists(path))
            throw new FileNotFoundException($"There's no file at {path}.", path);
        RomImage rom;
        try
        {
            rom = RomImage.Load(path);
        }
        catch (UnsupportedRomException e)
        {
            throw new InvalidDataException(
                $"This N64 ROM (\"{e.Header.Name.Trim()}\") isn't a ROM of Aidyn Chronicles: The First Mage that HBHT knows. Its SHA-1 is {e.Sha1}; NA 1.0's is {RomVersions.Na10.Sha1}.", e);
        }
        catch (InvalidDataException e)
        {
            throw new InvalidDataException($"This file isn't an N64 ROM. ({e.Message})", e);
        }
        if (rom.Version.Id != RomVersionId.Na10)
            throw new InvalidDataException($"This is the {rom.Version.Name} ROM. A North American 1.0 ROM is required.");
        return rom;
    }
}
