using System;
using System.Collections.Generic;
using GunconUSB;
using Guncon3Console.GunStates;

namespace Guncon3Console.Feeders
{
    internal abstract class BaseFeeder<TMapping> : IFeeder
    {
        protected readonly Dictionary<GunButton, TMapping> _mapping = new Dictionary<GunButton, TMapping>();

        public abstract string Name { get; }
        public abstract bool IsConnected { get; }

        public virtual void Log(string message) => Console.WriteLine($"[{Name}]: {message}");

        public virtual void ClearMapping() => _mapping.Clear();
        public virtual int MappingCount() => _mapping.Count;
        public virtual void AddMapping(GunButton gunButton, dynamic mapping)
        {
            var converted = ConvertMapping(mapping);
            if (converted != null)
                _mapping[gunButton] = converted;
        }
        protected virtual TMapping ConvertMapping(dynamic mapping)
        {
            if (mapping is TMapping typedMapping)
                return typedMapping;
            return default(TMapping);
        }
        public virtual dynamic GetMapping(GunButton gunButton)
        {
            if (_mapping.TryGetValue(gunButton, out var v))
                return v;
            return null;
        }

        public abstract void Connect();
        public abstract void Disconnect();
        public abstract void Feed(IGunState state);
    }

    internal abstract class BaseDisposableFeeder<TMapping> : BaseFeeder<TMapping>, IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (!_disposed)
            {
                Dispose(true);
                GC.SuppressFinalize(this);
                _disposed = true;
            }
        }
        protected virtual void Dispose(bool disposing) { }
    }
}
