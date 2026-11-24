namespace GtaAll.Core.Formats;

/// <summary>Niveau de certitude, repris de la documentation GTA6-Asset-Info.</summary>
public enum Confidence { Confirmed, Inferred, Unknown }

public sealed record HeaderField(int Offset, int Size, string Name, string Value, Confidence Confidence);
