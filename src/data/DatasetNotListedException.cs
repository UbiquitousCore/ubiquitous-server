namespace Ubiquitous.Data;

public sealed class DatasetNotListedException(string dataset)
    : FileNotFoundException($"Dataset '{dataset}' is not listed in the manifest.");
