using fNbt;

namespace LceWorldConverter;

public enum JavaChunkFormat
{
    Unknown,
    LegacyBlockArray,
    LegacyAnvil,
    ModernPalette,
    ModernExtendedHeight,
}

public readonly record struct JavaChunkFormatInfo(
    JavaChunkFormat Format,
    bool HasLevelWrapper,
    int DataVersion,
    int? MinSectionY,
    int? MaxSectionY,
    bool HasModernEntityTags)
{
    public bool IsSectionBased => Format is JavaChunkFormat.LegacyAnvil or JavaChunkFormat.ModernPalette or JavaChunkFormat.ModernExtendedHeight;

    public bool UsesPaletteSections => Format is JavaChunkFormat.ModernPalette or JavaChunkFormat.ModernExtendedHeight;

    public bool UsesModernContentSchema => UsesPaletteSections || HasModernEntityTags;

    public bool RequiresSectionShift => Format == JavaChunkFormat.ModernExtendedHeight;
}

public static class JavaChunkFormatHelper
{
    public static JavaChunkFormatInfo Inspect(NbtCompound rootTag)
    {
        ArgumentNullException.ThrowIfNull(rootTag);

        bool hasLevelWrapper = rootTag.Get<NbtCompound>("Level") != null;
        NbtCompound sourceLevel = rootTag.Get<NbtCompound>("Level") ?? rootTag;

        int dataVersion = rootTag.Get<NbtInt>("DataVersion")?.Value
            ?? rootTag.Get<NbtCompound>("Level")?.Get<NbtInt>("DataVersion")?.Value
            ?? 0;

        bool hasModernEntityTags = sourceLevel.Contains("block_entities") || sourceLevel.Contains("entities");
        bool hasLegacyBlockArrays = sourceLevel.Contains("Blocks");

        NbtList? sections = sourceLevel.Get<NbtList>("Sections") ?? sourceLevel.Get<NbtList>("sections");
        bool hasSections = sections != null && sections.Count > 0;
        bool hasPaletteSections = false;
        int? minSectionY = null;
        int? maxSectionY = null;

        if (sections != null)
        {
            foreach (NbtTag sectionTag in sections)
            {
                if (sectionTag is not NbtCompound section)
                    continue;

                int? sectionY = ReadSectionY(section);
                if (sectionY.HasValue)
                {
                    minSectionY = !minSectionY.HasValue ? sectionY.Value : Math.Min(minSectionY.Value, sectionY.Value);
                    maxSectionY = !maxSectionY.HasValue ? sectionY.Value : Math.Max(maxSectionY.Value, sectionY.Value);
                }

                if (SectionUsesPaletteStorage(section))
                    hasPaletteSections = true;
            }
        }

        JavaChunkFormat format = JavaChunkFormat.Unknown;
        if (hasPaletteSections)
        {
            bool hasExtendedHeightSections = (minSectionY.HasValue && minSectionY.Value < 0)
                || (maxSectionY.HasValue && maxSectionY.Value > 15);
            format = hasExtendedHeightSections
                ? JavaChunkFormat.ModernExtendedHeight
                : JavaChunkFormat.ModernPalette;
        }
        else if (hasSections)
        {
            format = JavaChunkFormat.LegacyAnvil;
        }
        else if (hasLegacyBlockArrays)
        {
            format = JavaChunkFormat.LegacyBlockArray;
        }

        return new JavaChunkFormatInfo(
            format,
            hasLevelWrapper,
            dataVersion,
            minSectionY,
            maxSectionY,
            hasModernEntityTags);
    }

    public static int? ReadSectionY(NbtCompound section)
    {
        ArgumentNullException.ThrowIfNull(section);

        NbtByte? byteY = section.Get<NbtByte>("Y");
        if (byteY != null)
            return unchecked((sbyte)byteY.Value);

        NbtInt? intY = section.Get<NbtInt>("Y");
        if (intY != null)
            return intY.Value;

        NbtShort? shortY = section.Get<NbtShort>("Y");
        if (shortY != null)
            return shortY.Value;

        return null;
    }

    private static bool SectionUsesPaletteStorage(NbtCompound section)
    {
        return section.Contains("block_states")
            || section.Contains("Palette")
            || section.Contains("BlockStates");
    }
}
