using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Collections.Generic;
using BepInEx.Logging;
using CloudpunkCoop.Game;
using CloudpunkCoop.Net;
using UnityEngine;

namespace CloudpunkCoop.UI
{
    /// <summary>
    /// Call Tick from Unity Update, before gameplay input, and Draw from OnGUI.
    /// Draw only queues commands; session lifecycle calls run once in Tick.
    /// Parent input patches should suppress native keyboard/actions while CapturesInput.
    /// This overlay never pauses the world or changes the configured startup role.
    /// </summary>
    public static class CoopConnectionMenu
    {
        private enum Command { None, Host, Join, Disconnect }

        private static readonly ManualLogSource Log = BepInEx.Logging.Logger.CreateLogSource("Cloudpunk Coop Menu");
        private static readonly Color Cyan = new Color(0.25f, 0.88f, 0.94f, 1f);
        private static readonly Color Magenta = new Color(0.94f, 0.39f, 0.68f, 1f);
        private static readonly Color Ink = new Color(0.08f, 0.11f, 0.17f, 1f);
        private static readonly Color Muted = new Color(0.66f, 0.73f, 0.80f, 1f);
        private const string AddressControl = "CloudpunkCoop.Address";
        private const string PortControl = "CloudpunkCoop.Port";
        private static string _address = "127.0.0.1", _port = "27050";
        private static string _error = "", _notice = "", _focus = "";
        private static string _localAddresses="";
        private static string _editControl="";
        private static int _caret;
        private static bool _selectAll;
        private static string _queuedAddress, _queuedPort;
        private static Command _command;
        private static bool _join, _initialized, _toggleRequested, _closeRequested, _clearFocus;
        private static bool _cursorSaved, _cursorVisible, _stylesAttempted, _guiFaultLogged;
        private static CursorLockMode _cursorLock;
        private static GUIStyle _title, _label, _small, _button, _field, _panel;

        public static bool Visible { get; private set; }

        // Deliberately capture the whole modal, including the first focus frame and buttons.
        // GUI focus alone arrives too late for native Update/input patches on a mouse click.
        public static bool CapturesInput => Visible;

        /// <summary>Unity main thread only. No networking is started by opening the menu.</summary>
        public static void Tick()
        {
            if (!_initialized)
            {
                _initialized = true;
                _address = string.IsNullOrWhiteSpace(Bootstrapper.CoopAddress) ? "127.0.0.1" : Bootstrapper.CoopAddress;
                int port = Bootstrapper.CoopPort;
                _port = (port >= 1 && port <= 65535 ? port : 27050).ToString(CultureInfo.InvariantCulture);
                _join = CoopSession.Role == CoopRole.Guest || CampaignReplication.GuestSaveIsolated;
            }

            if (DebugOverlay.Pressed(KeyCode.F9)) _toggleRequested = true;
            if (_closeRequested)
            {
                _closeRequested = false;
                _toggleRequested = false;
                SetVisible(false);
            }
            else if (_toggleRequested)
            {
                _toggleRequested = false;
                SetVisible(!Visible);
            }

            if (Visible) ReleaseCursor();
            Command command = _command;
            _command = Command.None; // Consume before calling APIs, including exceptions.
            if (command == Command.None) return;
            _error = "";
            _notice = "";
            try
            {
                if (command == Command.Disconnect)
                {
                    CoopSession.Stop();
                    _notice = "Disconnected. Co-op is Off.";
                    return;
                }
                if (CoopSession.Role != CoopRole.Off)
                {
                    _error = "Disconnect the current session before starting another.";
                    return;
                }
                if (command == Command.Host && CampaignReplication.GuestSaveIsolated)
                {
                    _error = "Restart Cloudpunk to host: guest save isolation is still active.";
                    return;
                }
                if (!TryPort(_queuedPort, out int port))
                {
                    _error = "Enter a TCP port from 1 to 65535.";
                    _focus = PortControl;
                    return;
                }
                if (command == Command.Join)
                {
                    if (!TryAddress(_queuedAddress, out string address))
                    {
                        _error = "Enter the host's IPv4 address (for example 192.168.1.2).";
                        _focus = AddressControl;
                        return;
                    }
                    if (!CoopSession.StartGuest(address, port, Log)) _error = CoopSession.Status;
                    else _notice = "Connecting. The live status below reports the result.";
                }
                else if (!CoopSession.StartHost(port, Log)) _error = CoopSession.Status;
                else _notice = "Listening for a guest. Share your reachable IPv4 address and port.";
            }
            catch (Exception ex)
            {
                _error = "Session action failed: " + ex.Message;
                Log.LogWarning("Menu action failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void SetVisible(bool visible)
        {
            if (Visible == visible) return;
            if (visible)
            {
                try
                {
                    _cursorLock = Cursor.lockState;
                    _cursorVisible = Cursor.visible;
                    _cursorSaved = true;
                }
                catch (Exception ex)
                {
                    _cursorSaved = false;
                    Log.LogWarning("Cannot snapshot menu cursor: " + ex.Message);
                }
                Visible = true;
                _localAddresses=LocalAddresses();
                if (CampaignReplication.GuestSaveIsolated) _join = true;
                _focus = _join ? AddressControl : PortControl;
                ReleaseCursor();
            }
            else
            {
                Visible = false;
                _focus = "";
                _clearFocus = true;
                if (!_cursorSaved) return;
                _cursorSaved = false;
                try
                {
                    Cursor.lockState = _cursorLock;
                    Cursor.visible = _cursorVisible;
                }
                catch (Exception ex) { Log.LogWarning("Cannot restore menu cursor: " + ex.Message); }
            }
        }

        private static void ReleaseCursor()
        {
            try
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            catch (Exception ex)
            {
                _error = "Cursor unavailable: " + ex.Message;
                _closeRequested = true;
            }
        }

        /// <summary>
        /// GUI entry is visible alongside the native menu while its menuParent is active.
        /// Gameplay has a small F9 hint. No native hierarchy or texture APIs are modified.
        /// </summary>
        public static void Draw()
        {
            Matrix4x4 matrix = GUI.matrix;
            Color color = GUI.color, background = GUI.backgroundColor, content = GUI.contentColor;
            bool enabled = GUI.enabled;
            int depth = GUI.depth;
            try
            {
                GUI.depth = -100;
                GUI.enabled = true;
                GUI.color = Color.white;
                GUI.contentColor = Color.white;
                GUI.backgroundColor = Color.white;
                float scale = Mathf.Min(Screen.width / 960f, Screen.height / 540f);
                if (scale <= 0f) return;
                GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
                float width = Screen.width / scale, height = Screen.height / scale;
                EnsureStyles();
                if (_clearFocus)
                {
                    GUI.FocusControl("");
                    _clearFocus = false;
                }
                if (!Visible)
                {
                    DrawEntry(width, height);
                    return;
                }
                DrawPanel(new Rect((width - 720f) * 0.5f, (height - 476f) * 0.5f, 720f, 476f));
            }
            catch (Exception ex)
            {
                // Native IMGUI availability is checked by the parent's in-game smoke test.
                // A failed drawing path must not strand the cursor or captured input.
                _error = "Menu GUI unavailable: " + ex.Message;
                _closeRequested = true;
                if (!_guiFaultLogged)
                {
                    _guiFaultLogged = true;
                    Log.LogWarning("Connection menu GUI failed: " + ex.ToString());
                }
            }
            finally
            {
                GUI.matrix = matrix;
                GUI.color = color;
                GUI.backgroundColor = background;
                GUI.contentColor = content;
                GUI.enabled = enabled;
                GUI.depth = depth;
            }
        }

        private static bool NativeMenuVisible()
        {
            // Global may exist before GameRefs resolves a world, so read the singleton.
            try
            {
                var global = global::Global.Instance;
                return global != null && global.menuParent != null && global.menuParent.activeInHierarchy;
            }
            catch (Exception) { return false; }
        }

        private static void DrawEntry(float width, float height)
        {
            if (NativeMenuVisible())
            {
                Rect entry = new Rect(width - 302f, height - 96f, 278f, 72f);
                Box(entry, Ink);
                Label(new Rect(entry.x + 12f, entry.y + 8f, 254f, 20f), "CO-OP / " + RoleText(), Cyan, _small);
                if (Button(new Rect(entry.x + 12f, entry.y + 32f, 254f, 30f), "HOST / JOIN   [F9]", Cyan))
                    _toggleRequested = true;
            }
            else
            {
                Rect hint = new Rect(width - 236f, height - 38f, 220f, 24f);
                Box(hint, Ink);
                Label(hint, "  F9  /  CO-OP  /  " + RoleText(), Muted, _small);
            }
        }

        private static void DrawPanel(Rect rect)
        {
            bool idle = CoopSession.Role == CoopRole.Off && _command == Command.None;
            bool isolated = CampaignReplication.GuestSaveIsolated;
            // Handle navigation before TextField can consume Tab/Return/Escape.
            HandleKeyboard(idle && (_join || !isolated));
            Box(rect, Ink);
            Box(new Rect(rect.x, rect.y, rect.width, 3f), Cyan);
            float x = rect.x + 24f, y = rect.y + 16f, w = rect.width - 48f;
            Label(new Rect(x, y, w - 110f, 32f), "CLOUDPUNK / CO-OP", Cyan, _title);
            if (Button(new Rect(rect.xMax - 104f, y, 80f, 30f), "CLOSE", Muted)) _closeRequested = true;
            Label(new Rect(x, y + 34f, w, 20f), "A shared connection through the city.  F9 / Esc to close.", Muted, _small);
            y += 64f;

            GUI.enabled = idle && !isolated;
            if (Button(new Rect(x, y, w * 0.5f - 5f, 36f), "HOST", !_join ? Cyan : Muted)) SelectMode(false);
            GUI.enabled = idle;
            if (Button(new Rect(x + w * 0.5f + 5f, y, w * 0.5f - 5f, 36f), "JOIN", _join ? Magenta : Muted)) SelectMode(true);
            GUI.enabled = true;
            y += 46f;
            Label(new Rect(x, y, w, 22f), _join ? "JOIN / Host's IPv4 address" : "HOST / Listen on all IPv4 interfaces", Color.white, _label);
            y += 26f;

            GUI.enabled = idle;
            if (_join)
            {
                _address = TextField(new Rect(x, y, w - 132f, 34f), _address, 64,AddressControl);
            }
            else Label(new Rect(x, y + 4f, w - 132f, 40f), "LAN / VPN IPv4: " + _localAddresses, Muted, _small);
            _port = TextField(new Rect(x + w - 120f, y, 120f, 34f), _port, 5,PortControl);
            GUI.enabled = true;
            Label(new Rect(x + w - 120f, y + 36f, 120f, 18f), "TCP PORT", Muted, _small);
            y += 61f;

            GUI.enabled = idle && (_join || !isolated);
            if (Button(new Rect(x, y, w, 38f), _join ? "CONNECT TO HOST" : "START HOST", _join ? Magenta : Cyan))
                Queue(_join ? Command.Join : Command.Host);
            GUI.enabled = true;
            y += 48f;

            Box(new Rect(x, y, w, 58f), new Color(0.16f, 0.21f, 0.29f, 1f));
            Label(new Rect(x + 12f, y + 5f, w - 158f, 20f), RoleText() + " / " + ConnectionText(), Cyan, _small);
            Label(new Rect(x + 12f, y + 25f, w - 158f, 30f), CoopSession.Status ?? "off", Color.white, _small);
            GUI.enabled = !idle && _command == Command.None;
            if (Button(new Rect(x + w - 134f, y + 13f, 122f, 32f), "DISCONNECT", Magenta)) Queue(Command.Disconnect);
            GUI.enabled = true;
            y += 65f;

            Label(new Rect(x, y, w, 34f), string.IsNullOrEmpty(_error) ? _notice : _error,
                string.IsNullOrEmpty(_error) ? Cyan : Magenta, _small);
            y += 36f;
            string saveInfo = isolated
                ? "Guest save isolation is active until Cloudpunk restarts, even after disconnect. Join again is allowed; restart to host or save solo progress."
                : "Joining isolates guest solo saves until Cloudpunk restarts, even after disconnect. Host uses its own campaign.";
            Label(new Rect(x, y, w, 38f), saveInfo, isolated ? Magenta : Muted, _small);
            y += 40f;
            Label(new Rect(x, y, w, 40f), "Use the host's LAN IPv4 on the same network, VPN IPv4 over a shared VPN, or a publicly routable IPv4 with TCP access. Both players use the same port.", Muted, _small);

            if (!string.IsNullOrEmpty(_focus) && idle)
            {
                _editControl=_focus;_caret=(_focus==AddressControl ? _address : _port).Length;_selectAll=true;
                _focus = "";
            }
            // Consume mouse events inside the modal after its controls have seen them.
            Event current = Event.current;
            if (current != null && rect.Contains(current.mousePosition) &&
                (current.type == EventType.MouseDown || current.type == EventType.MouseUp || current.type == EventType.ScrollWheel))
                current.Use();
        }

        private static void SelectMode(bool join)
        {
            _join = join;
            _error = _notice = "";
            _focus = join ? AddressControl : PortControl;
        }

        private static void HandleKeyboard(bool canStart)
        {
            Event current = Event.current;
            if (current == null || current.type != EventType.KeyDown) return;
            if (current.keyCode == KeyCode.Escape)
            {
                _closeRequested = true;
                current.Use();
            }
            else if (current.keyCode == KeyCode.Tab)
            {
                _focus = _join && _editControl == PortControl ? AddressControl : PortControl;
                current.Use();
            }
            else if ((current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter) && canStart)
            {
                Queue(_join ? Command.Join : Command.Host);
                current.Use();
            }
        }

        private static void Queue(Command command)
        {
            if (_command != Command.None) return;
            _queuedAddress = _address;
            _queuedPort = _port;
            _command = command;
        }

        private static bool TryPort(string text, out int port) =>
            int.TryParse((text ?? "").Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out port) && port >= 1 && port <= 65535;

        private static bool TryAddress(string text, out string address)
        {
            address = "";
            string[] parts = (text ?? "").Trim().Split('.');
            if (parts.Length != 4) return false;
            var bytes = new byte[4];
            for (int i = 0; i < parts.Length; i++)
                if (!byte.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out bytes[i])) return false;
            // Any, broadcast and multicast are not unicast host endpoints. Loopback is
            // useful for local testing; LAN/VPN/public IPv4 addresses all remain valid.
            if (bytes[0] == 0 || bytes[0] >= 224) return false;
            var parsed = new IPAddress(bytes);
            if (parsed.AddressFamily != AddressFamily.InterNetwork) return false;
            address = parsed.ToString();
            return true;
        }

        private static string RoleText() => CoopSession.Role == CoopRole.Off ? "OFF" : CoopSession.Role.ToString().ToUpperInvariant();
        private static string LocalAddresses()
        {
            var addresses=new List<string>();
            try
            {
                foreach(var adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if(adapter.OperationalStatus!=OperationalStatus.Up || adapter.NetworkInterfaceType==NetworkInterfaceType.Loopback)continue;
                    foreach(var entry in adapter.GetIPProperties().UnicastAddresses)
                        if(entry.Address.AddressFamily==AddressFamily.InterNetwork && !IPAddress.IsLoopback(entry.Address) && addresses.Count<4)
                            addresses.Add(entry.Address.ToString());
                }
            }
            catch(Exception){return "unavailable - check your network adapter";}
            return addresses.Count==0 ? "no active IPv4 adapter" : string.Join(" / ",addresses);
        }
        private static string ConnectionText() => CoopSession.PeerOnline ? "CONNECTED" :
            CoopSession.Role == CoopRole.Host ? "WAITING FOR GUEST" : CoopSession.Role == CoopRole.Guest ? "CONNECTING / RETRYING" : "READY";

        private static void EnsureStyles()
        {
            if (_stylesAttempted) return;
            _stylesAttempted = true;
            try
            {
                _panel = new GUIStyle(GUI.skin.box);
                _title = new GUIStyle(GUI.skin.label) { fontSize = 24, richText = false };
                _label = new GUIStyle(GUI.skin.label) { fontSize = 16, richText = false, wordWrap = true };
                _small = new GUIStyle(GUI.skin.label) { fontSize = 13, richText = false, wordWrap = true };
                _button = new GUIStyle(GUI.skin.button) { fontSize = 14, richText = false };
                _field = new GUIStyle(GUI.skin.textField) { fontSize = 18, richText = false };
            }
            catch (Exception ex)
            {
                _panel = _title = _label = _small = _button = _field = null;
                Log.LogWarning("Menu styles unavailable; using native default skin: " + ex.Message);
            }
        }

        private static void Box(Rect rect, Color tint)
        {
            Color previous = GUI.backgroundColor;
            Color previousColor=GUI.color;
            try
            {
                GUI.color=new Color(tint.r,tint.g,tint.b,0.96f);
                NativeDialoguePortrait.Draw(rect,Texture2D.whiteTexture,new Rect(0,0,1,1));
                GUI.color=previousColor;
                GUI.backgroundColor = tint;
                if (_panel != null) GUI.Box(rect, "", _panel);
                else GUI.Box(rect, "");
            }
            finally { GUI.backgroundColor = previous;GUI.color=previousColor; }
        }

        private static void Label(Rect rect, string text, Color tint, GUIStyle style)
        {
            Color previous = GUI.contentColor;
            try
            {
                GUI.contentColor = tint;
                if (style != null) GUI.Label(rect, text, style);
                else GUI.Label(rect, text);
            }
            finally { GUI.contentColor = previous; }
        }

        private static bool Button(Rect rect, string text, Color tint)
        {
            Color previous = GUI.contentColor;
            try
            {
                GUI.contentColor = tint;
                return _button != null ? GUI.Button(rect, text, _button) : GUI.Button(rect, text);
            }
            finally { GUI.contentColor = previous; }
        }

        // The shipped TextField wrapper reconstructs a stripped TextEditor through
        // GetStateObject/RuntimeTypeHandle. Keep editing state in managed memory;
        // retain native IMGUI event delivery and the game's own text-field styling.
        private static string TextField(Rect rect,string text,int maxLength,string control)
        {
            Event current=Event.current;
            if(GUI.enabled && current!=null)
            {
                if(current.type==EventType.MouseDown && rect.Contains(current.mousePosition))
                {_editControl=control;_caret=text.Length;_selectAll=true;current.Use();}
                else if(current.type==EventType.KeyDown && _editControl==control)
                {
                    _caret=Mathf.Clamp(_caret,0,text.Length);
                    bool consumed=true;
                    if(current.control && current.keyCode==KeyCode.A)_selectAll=true;
                    else if(current.control && current.keyCode==KeyCode.C)GUIUtility.systemCopyBuffer=_selectAll ? text : "";
                    else if(current.control && current.keyCode==KeyCode.V)
                    {
                        string paste=GUIUtility.systemCopyBuffer ?? "";
                        paste=paste.Replace("\r","").Replace("\n","");
                        Insert(paste);
                    }
                    else if(current.keyCode==KeyCode.Home){_caret=0;_selectAll=false;}
                    else if(current.keyCode==KeyCode.End){_caret=text.Length;_selectAll=false;}
                    else if(current.keyCode==KeyCode.LeftArrow){_caret=Math.Max(0,_caret-1);_selectAll=false;}
                    else if(current.keyCode==KeyCode.RightArrow){_caret=Math.Min(text.Length,_caret+1);_selectAll=false;}
                    else if(current.keyCode==KeyCode.Backspace)
                    {if(_selectAll){text="";_caret=0;_selectAll=false;}else if(_caret>0){text=text.Remove(--_caret,1);}}
                    else if(current.keyCode==KeyCode.Delete)
                    {if(_selectAll){text="";_caret=0;_selectAll=false;}else if(_caret<text.Length)text=text.Remove(_caret,1);}
                    else if(!current.control && !char.IsControl(current.character))Insert(current.character.ToString());
                    else consumed=false;
                    if(consumed)current.Use();
                }
            }
            string display=text;
            if(GUI.enabled && _editControl==control && !_selectAll && Time.realtimeSinceStartup%1f<0.5f)
                display=text.Insert(Mathf.Clamp(_caret,0,text.Length),"|");
            Color previous=GUI.contentColor;
            try
            {
                GUI.contentColor=GUI.enabled && _editControl==control ? (_selectAll ? Magenta : Cyan) : Color.white;
                if(_field!=null)GUI.Box(rect,display,_field);else GUI.Box(rect,display);
            }
            finally{GUI.contentColor=previous;}
            return text;
            void Insert(string value)
            {
                if(_selectAll){text="";_caret=0;_selectAll=false;}
                int count=Math.Min(value.Length,maxLength-text.Length);
                if(count<=0)return;
                text=text.Insert(_caret,value.Substring(0,count));_caret+=count;
            }
        }
    }
}
