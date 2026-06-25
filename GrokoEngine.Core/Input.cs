using System;
using System.Collections.Generic;
using Vector2 = MiMotor.Mathematics.Vector2;

namespace GrokoEngine
{
    /// <summary>
    /// Códigos de tecla independientes de plataforma.
    /// El editor (ImGuiEditor, WPF, etc.) convierte sus propias teclas
    /// a este enum antes de llamar a RegisterKeyDown / RegisterKeyUp.
    /// </summary>
    public enum KeyCode
    {
        Unknown = 0,
        W, A, S, D,
        Q, E, R, F,
        Z, X, C, V,
        Tab,
        Left, Right, Up, Down,
        Space, Escape, Enter, Backspace,
        LeftShift, RightShift,
        LeftControl, RightControl,
        LeftAlt, RightAlt,
        Delete, Home, End,
        F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
        Alpha0, Alpha1, Alpha2, Alpha3, Alpha4,
        Alpha5, Alpha6, Alpha7, Alpha8, Alpha9
    }

    public enum MouseButton
    {
        Left   = 0,
        Right  = 1,
        Middle = 2
    }

    /// <summary>
    /// Sistema de input del motor. No depende de ninguna plataforma.
    /// El editor inyecta los eventos mediante Register* y los scripts
    /// los consultan via GetKey / GetAxis / etc.
    /// </summary>
    public static class Input
    {
        private static readonly HashSet<KeyCode> _keysHeld = new();
        private static readonly HashSet<KeyCode> _keysDown = new();
        private static readonly HashSet<KeyCode> _keysUp   = new();

        private static readonly bool[] _mouseHeld = new bool[3];
        private static readonly bool[] _mouseDown = new bool[3];
        private static readonly bool[] _mouseUp   = new bool[3];

        private static Vector2 _mousePosition     = Vector2.Zero;
        private static Vector2 _lastMousePosition  = Vector2.Zero;
        private static Vector2 _mouseDelta         = Vector2.Zero;
        private static float   _mouseScrollDelta   = 0f;
        private static bool    _cursorLocked       = false;

        // ── Propiedades públicas ───────────────────────────────────
        public static Vector2 MousePosition    => _mousePosition;
        public static Vector2 MouseDelta       => _mouseDelta;
        public static float   MouseScrollDelta => _mouseScrollDelta;
        public static bool    CursorLocked     => _cursorLocked;

        // ── Consulta de teclas ─────────────────────────────────────
        public static bool GetKey(KeyCode key)     => _keysHeld.Contains(key);
        public static bool GetKeyDown(KeyCode key) => _keysDown.Contains(key);
        public static bool GetKeyUp(KeyCode key)   => _keysUp.Contains(key);

        /// <summary>Consulta por nombre de tecla ("W", "Space", etc.).</summary>
        public static bool GetKey(string keyName)
        {
            var k = ParseKey(keyName);
            return k != KeyCode.Unknown && GetKey(k);
        }

        public static bool GetKeyDown(string keyName)
        {
            var k = ParseKey(keyName);
            return k != KeyCode.Unknown && GetKeyDown(k);
        }

        public static bool GetKeyUp(string keyName)
        {
            var k = ParseKey(keyName);
            return k != KeyCode.Unknown && GetKeyUp(k);
        }

        // ── Consulta de ratón ──────────────────────────────────────
        public static bool GetMouseButton(int btn)     => IsValidBtn(btn) && _mouseHeld[btn];
        public static bool GetMouseButtonDown(int btn) => IsValidBtn(btn) && _mouseDown[btn];
        public static bool GetMouseButtonUp(int btn)   => IsValidBtn(btn) && _mouseUp[btn];

        public static bool GetMouseButton(MouseButton btn)     => GetMouseButton((int)btn);
        public static bool GetMouseButtonDown(MouseButton btn) => GetMouseButtonDown((int)btn);
        public static bool GetMouseButtonUp(MouseButton btn)   => GetMouseButtonUp((int)btn);

        // ── Ejes virtuales ─────────────────────────────────────────
        public static float GetAxis(string axis) => axis.ToLowerInvariant() switch
        {
            "horizontal"       => GetKey(KeyCode.A) || GetKey(KeyCode.Left)  ? -1f
                                : GetKey(KeyCode.D) || GetKey(KeyCode.Right) ?  1f : 0f,
            "vertical"         => GetKey(KeyCode.S) || GetKey(KeyCode.Down)  ? -1f
                                : GetKey(KeyCode.W) || GetKey(KeyCode.Up)    ?  1f : 0f,
            "jump"             => GetKey(KeyCode.Space) ? 1f : 0f,
            "fire1"            => GetMouseButton(MouseButton.Left)  ? 1f : 0f,
            "fire2"            => GetMouseButton(MouseButton.Right) ? 1f : 0f,
            "mouse x"          => _mouseDelta.X,
            "mouse y"          => _mouseDelta.Y,
            "mouse scrollwheel"=> _mouseScrollDelta,
            _                  => 0f
        };

        public static float GetAxisRaw(string axis) => GetAxis(axis);

        // ── Registro de eventos (llamado por el editor) ────────────
        public static void RegisterKeyDown(KeyCode key)
        {
            if (_keysHeld.Contains(key)) return;
            _keysHeld.Add(key);
            _keysDown.Add(key);
        }

        public static void RegisterKeyUp(KeyCode key)
        {
            if (!_keysHeld.Contains(key)) return;
            _keysHeld.Remove(key);
            _keysUp.Add(key);
        }

        public static void RegisterKeyState(KeyCode key, bool pressed)
        {
            if (pressed) RegisterKeyDown(key);
            else         RegisterKeyUp(key);
        }

        public static void RegisterMouseDown(MouseButton btn)   => RegisterMouseState((int)btn, true);
        public static void RegisterMouseUp(MouseButton btn)     => RegisterMouseState((int)btn, false);

        public static void RegisterMouseState(int btn, bool pressed)
        {
            if (!IsValidBtn(btn)) return;
            if (pressed)
            {
                if (_mouseHeld[btn]) return;
                _mouseHeld[btn] = true;
                _mouseDown[btn] = true;
            }
            else
            {
                if (!_mouseHeld[btn]) return;
                _mouseHeld[btn] = false;
                _mouseUp[btn]   = true;
            }
        }

        public static void RegisterMouseState(MouseButton btn, bool pressed)
            => RegisterMouseState((int)btn, pressed);

        public static void RegisterMouseMove(float x, float y)
        {
            _mousePosition = new Vector2(x, y);
            _mouseDelta    = _mousePosition - _lastMousePosition;
        }

        public static void RegisterMouseDelta(float dx, float dy)
            => _mouseDelta = new Vector2(dx, dy);

        public static void RegisterMouseScroll(float delta)
            => _mouseScrollDelta = delta;

        public static void LockCursor()
        {
            _cursorLocked = true;
            _lastMousePosition = _mousePosition;
            _mouseDelta = Vector2.Zero;
        }

        public static void UnlockCursor()
        {
            _cursorLocked = false;
            _lastMousePosition = _mousePosition;
            _mouseDelta = Vector2.Zero;
        }

        public static void SetCursorLocked(bool locked)
        {
            if (locked) LockCursor();
            else UnlockCursor();
        }

        /// <summary>
        /// Llama una vez al final de cada frame para limpiar los estados one-shot.
        /// </summary>
        public static void Flush()
        {
            _keysDown.Clear();
            _keysUp.Clear();
            _mouseDown[0] = _mouseDown[1] = _mouseDown[2] = false;
            _mouseUp[0]   = _mouseUp[1]   = _mouseUp[2]   = false;
            _lastMousePosition = _mousePosition;
            _mouseDelta        = Vector2.Zero;
            _mouseScrollDelta  = 0f;
        }

        // ── Helpers privados ───────────────────────────────────────
        private static bool IsValidBtn(int btn) => btn >= 0 && btn < _mouseHeld.Length;

        private static readonly Dictionary<string, KeyCode> _keyNameMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["W"]            = KeyCode.W,      ["A"] = KeyCode.A,
                ["S"]            = KeyCode.S,      ["D"] = KeyCode.D,
                ["Q"]            = KeyCode.Q,      ["E"] = KeyCode.E,
                ["R"]            = KeyCode.R,      ["F"] = KeyCode.F,
                ["Z"]            = KeyCode.Z,      ["X"] = KeyCode.X,
                ["C"]            = KeyCode.C,      ["V"] = KeyCode.V,
                ["Tab"]          = KeyCode.Tab,
                ["Left"]         = KeyCode.Left,   ["Right"]  = KeyCode.Right,
                ["Up"]           = KeyCode.Up,     ["Down"]   = KeyCode.Down,
                ["Space"]        = KeyCode.Space,  ["Escape"] = KeyCode.Escape,
                ["Enter"]        = KeyCode.Enter,  ["Return"] = KeyCode.Enter,
                ["Backspace"]    = KeyCode.Backspace,
                ["LeftShift"]    = KeyCode.LeftShift,
                ["RightShift"]   = KeyCode.RightShift,
                ["LeftControl"]  = KeyCode.LeftControl,
                ["RightControl"] = KeyCode.RightControl,
                ["LeftAlt"]      = KeyCode.LeftAlt,
                ["RightAlt"]     = KeyCode.RightAlt,
                ["Delete"]       = KeyCode.Delete,
                ["Home"]         = KeyCode.Home,
                ["End"]          = KeyCode.End,
                ["F1"]  = KeyCode.F1,  ["F2"]  = KeyCode.F2,
                ["F3"]  = KeyCode.F3,  ["F4"]  = KeyCode.F4,
                ["F5"]  = KeyCode.F5,  ["F6"]  = KeyCode.F6,
                ["F7"]  = KeyCode.F7,  ["F8"]  = KeyCode.F8,
                ["F9"]  = KeyCode.F9,  ["F10"] = KeyCode.F10,
                ["F11"] = KeyCode.F11, ["F12"] = KeyCode.F12,
                ["0"]   = KeyCode.Alpha0, ["1"] = KeyCode.Alpha1,
                ["2"]   = KeyCode.Alpha2, ["3"] = KeyCode.Alpha3,
                ["4"]   = KeyCode.Alpha4, ["5"] = KeyCode.Alpha5,
                ["6"]   = KeyCode.Alpha6, ["7"] = KeyCode.Alpha7,
                ["8"]   = KeyCode.Alpha8, ["9"] = KeyCode.Alpha9,
            };

        /// <summary>
        /// Convierte un nombre de tecla a KeyCode.
        /// Devuelve KeyCode.Unknown si no reconoce el nombre
        /// (nunca lanza excepción).
        /// </summary>
        public static KeyCode ParseKey(string name)
        {
            if (string.IsNullOrEmpty(name)) return KeyCode.Unknown;
            if (_keyNameMap.TryGetValue(name, out var k)) return k;
            // Fallback: intentar parsear el enum directamente
            return Enum.TryParse<KeyCode>(name, ignoreCase: true, out var result)
                ? result : KeyCode.Unknown;
        }
    }
}
