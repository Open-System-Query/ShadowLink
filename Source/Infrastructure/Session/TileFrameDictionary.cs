using System;
using System.Collections.Generic;

namespace ShadowLink.Infrastructure.Session;

internal sealed class TileFrameDictionary
{
    private readonly Int64 _maximumBytes;
    private readonly Dictionary<TileDictionaryKey, Int32> _keyToId;
    private readonly Dictionary<Int32, TileFrameDictionaryEntry> _entriesById;
    private readonly LinkedList<Int32> _lruIds;
    private Int64 _currentBytes;
    private Int32 _nextId;

    public TileFrameDictionary(Int64 maximumBytes)
    {
        _maximumBytes = Math.Max(4L * 1024L * 1024L, maximumBytes);
        _keyToId = new Dictionary<TileDictionaryKey, Int32>();
        _entriesById = new Dictionary<Int32, TileFrameDictionaryEntry>();
        _lruIds = new LinkedList<Int32>();
        _nextId = 1;
    }

    public Boolean TryGetId(TileDictionaryKey key, out Int32 dictionaryId, Boolean touch)
    {
        if (_keyToId.TryGetValue(key, out dictionaryId) &&
            _entriesById.TryGetValue(dictionaryId, out TileFrameDictionaryEntry? entry) &&
            entry.Bytes is not null)
        {
            if (touch)
            {
                TouchById(dictionaryId);
            }

            return true;
        }

        dictionaryId = 0;
        return false;
    }

    public Boolean TryGetById(Int32 dictionaryId, out Byte[]? bytes, Boolean touch)
    {
        if (_entriesById.TryGetValue(dictionaryId, out TileFrameDictionaryEntry? entry) && entry.Bytes is not null)
        {
            if (touch)
            {
                TouchById(dictionaryId);
            }

            bytes = entry.Bytes;
            return true;
        }

        bytes = null;
        return false;
    }

    public Int32 ReserveId(TileDictionaryKey key)
    {
        if (_keyToId.TryGetValue(key, out Int32 existingId))
        {
            return existingId;
        }

        Int32 dictionaryId = _nextId++;
        _keyToId.Add(key, dictionaryId);
        _entriesById.Add(dictionaryId, new TileFrameDictionaryEntry(dictionaryId, key, true, null, null));
        return dictionaryId;
    }

    public void TouchById(Int32 dictionaryId)
    {
        if (!_entriesById.TryGetValue(dictionaryId, out TileFrameDictionaryEntry? entry) ||
            entry.Node is null ||
            ReferenceEquals(_lruIds.First, entry.Node))
        {
            return;
        }

        _lruIds.Remove(entry.Node);
        LinkedListNode<Int32> node = _lruIds.AddFirst(dictionaryId);
        _entriesById[dictionaryId] = entry.WithNode(node);
    }

    public void Add(TileDictionaryKey key, Int32 dictionaryId, Byte[] bytes)
    {
        if (bytes.Length <= 0 || bytes.Length > _maximumBytes)
        {
            return;
        }

        if (!_keyToId.TryGetValue(key, out Int32 existingId))
        {
            _keyToId.Add(key, dictionaryId);
        }
        else
        {
            dictionaryId = existingId;
        }

        _nextId = Math.Max(_nextId, dictionaryId + 1);

        if (_entriesById.TryGetValue(dictionaryId, out TileFrameDictionaryEntry? existingEntry) && existingEntry.Bytes is not null)
        {
            TouchById(dictionaryId);
            return;
        }

        Byte[] storedBytes = new Byte[bytes.Length];
        Buffer.BlockCopy(bytes, 0, storedBytes, 0, bytes.Length);
        LinkedListNode<Int32> node = _lruIds.AddFirst(dictionaryId);
        _entriesById[dictionaryId] = new TileFrameDictionaryEntry(dictionaryId, key, true, storedBytes, node);
        _currentBytes += storedBytes.Length;

        while (_currentBytes > _maximumBytes && _lruIds.Last is not null)
        {
            Int32 oldestDictionaryId = _lruIds.Last.Value;
            _lruIds.RemoveLast();
            if (_entriesById.Remove(oldestDictionaryId, out TileFrameDictionaryEntry? removedEntry) && removedEntry.Bytes is not null)
            {
                _currentBytes -= removedEntry.Bytes.Length;
                if (removedEntry.HasKey)
                {
                    _keyToId.Remove(removedEntry.Key);
                }
            }
        }
    }

    public void AddKnown(Int32 dictionaryId, Byte[] bytes)
    {
        if (bytes.Length <= 0 || bytes.Length > _maximumBytes)
        {
            return;
        }

        _nextId = Math.Max(_nextId, dictionaryId + 1);

        if (_entriesById.TryGetValue(dictionaryId, out TileFrameDictionaryEntry? existingEntry) && existingEntry.Bytes is not null)
        {
            TouchById(dictionaryId);
            return;
        }

        Byte[] storedBytes = new Byte[bytes.Length];
        Buffer.BlockCopy(bytes, 0, storedBytes, 0, bytes.Length);
        LinkedListNode<Int32> node = _lruIds.AddFirst(dictionaryId);
        Boolean hasKey = existingEntry?.HasKey == true;
        TileDictionaryKey key = existingEntry?.Key ?? default;
        _entriesById[dictionaryId] = new TileFrameDictionaryEntry(dictionaryId, key, hasKey, storedBytes, node);
        _currentBytes += storedBytes.Length;

        while (_currentBytes > _maximumBytes && _lruIds.Last is not null)
        {
            Int32 oldestDictionaryId = _lruIds.Last.Value;
            _lruIds.RemoveLast();
            if (_entriesById.Remove(oldestDictionaryId, out TileFrameDictionaryEntry? removedEntry) && removedEntry.Bytes is not null)
            {
                _currentBytes -= removedEntry.Bytes.Length;
                if (removedEntry.HasKey)
                {
                    _keyToId.Remove(removedEntry.Key);
                }
            }
        }
    }
}
