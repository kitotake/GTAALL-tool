namespace GtaAll.Textures.Decoding;

/// <summary>
/// Cache LRU des blocs de l'espace virtuel d'un magasin TTPF déjà décompressés. Plusieurs textures partagent souvent le même
/// bloc zlib : sans cache, chacune le relit et le décompresse à nouveau. Un bloc dont la décompression a échoué est mémorisé
/// (valeur null) pour ne pas être réessayé. À utiliser avec un seul fichier TTPF à la fois ; non prévu pour plusieurs threads.
/// </summary>
public sealed class TtpfChunkCache
{
    private readonly long _budget;
    private long _bytes;
    private readonly Dictionary<int, LinkedListNode<(int Key, byte[]? Data)>> _map = new();
    private readonly LinkedList<(int Key, byte[]? Data)> _lru = new();

    public int Hits { get; private set; }
    public int Misses { get; private set; }

    /// <param name="budgetBytes">Mémoire maximale occupée par les blocs mémorisés (512 Mo par défaut).</param>
    public TtpfChunkCache(long budgetBytes = 512L << 20) => _budget = Math.Max(0, budgetBytes);

    public bool TryGet(int key, out byte[]? data)
    {
        if (_map.TryGetValue(key, out var node))
        {
            _lru.Remove(node); _lru.AddFirst(node);
            data = node.Value.Data; Hits++; return true;
        }
        data = null; Misses++; return false;
    }

    public void Put(int key, byte[]? data)
    {
        long size = data?.Length ?? 0;
        if (size > _budget || _map.ContainsKey(key)) return; // trop gros pour le cache : décodé mais non mémorisé
        _map[key] = _lru.AddFirst((key, data));
        _bytes += size;
        while (_bytes > _budget && _lru.Last is { } last)
        {
            _bytes -= last.Value.Data?.Length ?? 0;
            _map.Remove(last.Value.Key);
            _lru.RemoveLast();
        }
    }
}
