#if NETFRAMEWORK
using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LaserEnergyMonitor.Infrastructure.Ophir
{
    internal static class OphirFastXActiveXHost
    {
        public static OphirFastXRuntimeHandle Create(Type runtimeType, string progId)
        {
            if (runtimeType == null)
            {
                throw new ArgumentNullException("runtimeType");
            }

            string clsid = ResolveClsid(progId, runtimeType);
            HostedControlLease lease = null;
            try
            {
                lease = new HostedControlLease(clsid);
                return OphirFastXRuntimeHandle.ForHostedActiveX(
                    lease.ControlObject,
                    "Hosted WinForms ActiveX control (" + progId + ", CLSID " + clsid + ")",
                    lease);
            }
            catch
            {
                if (lease != null)
                {
                    lease.Dispose();
                }

                throw;
            }
        }

        private static string ResolveClsid(string progId, Type runtimeType)
        {
            if (!string.IsNullOrWhiteSpace(progId))
            {
                using (RegistryKey key = Registry.ClassesRoot.OpenSubKey(progId + "\\CLSID"))
                {
                    object value = key != null ? key.GetValue(null) : null;
                    string clsid = value as string;
                    if (!string.IsNullOrWhiteSpace(clsid))
                    {
                        return clsid.Trim('{', '}');
                    }
                }
            }

            Guid guid = runtimeType.GUID;
            if (guid == Guid.Empty)
            {
                throw new InvalidOperationException("OphirFastX ActiveX CLSID could not be resolved from ProgID: " + progId);
            }

            return guid.ToString("D");
        }

        private sealed class OphirFastXAxHost : AxHost
        {
            public OphirFastXAxHost(string clsid)
                : base(clsid)
            {
            }

            public object ControlObject
            {
                get { return GetOcx(); }
            }
        }

        private sealed class HostedControlLease : IDisposable
        {
            private readonly Form _form;
            private readonly OphirFastXAxHost _control;
            private bool _disposed;

            public HostedControlLease(string clsid)
            {
                _form = new Form
                {
                    FormBorderStyle = FormBorderStyle.FixedToolWindow,
                    ShowInTaskbar = false,
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(-32000, -32000),
                    Size = new Size(1, 1),
                    Opacity = 0d
                };

                _control = new OphirFastXAxHost(clsid)
                {
                    Enabled = true,
                    Location = new Point(0, 0),
                    Size = new Size(1, 1)
                };

                _form.Controls.Add(_control);
                _form.Show();
                _form.CreateControl();
                _control.CreateControl();
                Application.DoEvents();

                if (!_control.IsHandleCreated)
                {
                    throw new InvalidOperationException("OphirFastX ActiveX host was created, but its window handle was not initialized.");
                }

                ControlObject = _control.ControlObject;
                if (ControlObject == null)
                {
                    throw new InvalidOperationException("OphirFastX ActiveX host did not expose an automation object.");
                }
            }

            public object ControlObject { get; private set; }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                try
                {
                    if (!_control.IsDisposed)
                    {
                        _control.Dispose();
                    }
                }
                finally
                {
                    if (!_form.IsDisposed)
                    {
                        _form.Close();
                        _form.Dispose();
                    }
                }
            }
        }
    }
}
#endif
