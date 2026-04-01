using Guncon3Console.Common;
using Guncon3Console.Common.Hid;
using Guncon3Console.GunStates;
using Guncon3Console.TetherScript;
using GunconUSB;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

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

    // New base class for HID-based feeders
    internal abstract class BaseHidFeeder<TMapping, THidController> : BaseDisposableFeeder<TMapping>, IHidFeeder
        where THidController : IHidConnection, new()
    {
        protected readonly THidController _hid = new THidController();
        public virtual IHidConnection Hid => _hid;
        public abstract ushort VendorId { get; }
        public abstract ushort ProductId { get; }

        public override bool IsConnected => _hid.Connected;

        public override void Connect()
        {
            _hid.OnLog += OnHidLog;
            _hid.VendorID = VendorId;
            _hid.ProductID = ProductId;
            _hid.Connect();
            if (!_hid.Connected)
                throw new Exception($"Could not connect to HID device: {Name}");
        }

        public override void Disconnect()
        {
            _hid.Disconnect();
            _hid.OnLog -= OnHidLog;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Disconnect();
                _hid.Dispose();
            }
        }

        public virtual void OnHidLog(object sender, LogArgs e) => Log(e.Msg);
    }

    internal abstract class BaseTetherScriptFeeder<TMapping> : BaseHidFeeder<TMapping, TetherScript.HidController>, IHidFeeder
    {
        public override ushort VendorId => (ushort)DriversConst.TTC_VENDORID;

        protected void SendHidReport(byte[] report)
        {
            if (!IsConnected)
                throw new Exception($"Cannot send HID report, not connected: {Name}");
            _hid.SendData(report, (uint)report.Length);
        }
    }

    internal abstract class BaseTetherScriptMouseFeeder : BaseTetherScriptFeeder<MouseButton>, IMouseFeeder, IHidFeeder
    {
        public bool Force4by3 { get; set; } = false;
        protected byte _buttons { get; set; } = 0;

        // Compute mouse button mask from mapping and state
        public void ComputeButtonsMask(IGunState state)
        {
            _buttons = MouseFeederHelper.ComputeButtonsMask(_mapping, state);
        }
    }

    internal abstract class BaseVMultiFeeder<TMapping> : BaseHidFeeder<TMapping, vMulti.HidController>, IHidFeeder
    {
        public override ushort VendorId => (ushort)0x001F;
    }

    internal abstract class BaseVMultiMouseFeeder : BaseVMultiFeeder<MouseButton>, IMouseFeeder, IHidFeeder
    {
        public bool Force4by3 { get; set; } = false;
        protected byte _buttons { get; set; } = 0;

        // Compute mouse button mask from mapping and state
        public void ComputeButtonsMask(IGunState state)
        {
            _buttons = 0;
            foreach (var map in _mapping)
            {
                if (!state.BtnState.TryGetValue(map.Key, out bool pressed) || !pressed)
                    continue;

                switch (map.Value)
                {
                    case MouseButton.Left:
                        _buttons |= vMulti.HidController.MOUSE_BUTTON_1;
                        break;
                    case MouseButton.Right:
                        _buttons |= vMulti.HidController.MOUSE_BUTTON_2;
                        break;
                    case MouseButton.Middle:
                        _buttons |= vMulti.HidController.MOUSE_BUTTON_3;
                        break;
                }
            }
        }
    }
}
