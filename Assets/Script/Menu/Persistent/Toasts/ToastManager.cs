using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace YARG.Menu.Persistent
{
    public class ToastManager : MonoSingleton<ToastManager>
    {
        private const int MAX_TOAST_COUNT = 5;

        [Header("Sorting")]
        [SerializeField]
        private int _sortingOrder = 1000;

        [SerializeField]
        private Toast _toastPrefab;

        [Header("Colors")]
        [SerializeField]
        private Color _generalColor;
        [SerializeField]
        private Color _successColor;
        [SerializeField]
        private Color _warningColor;
        [SerializeField]
        private Color _informationColor;
        [SerializeField]
        private Color _errorColor;

        [Space]
        [Header("Icons")]
        [SerializeField]
        private Sprite _iconGeneral;
        [SerializeField]
        private Sprite _iconSuccess;
        [SerializeField]
        private Sprite _iconWarning;
        [SerializeField]
        private Sprite _iconInformation;
        [SerializeField]
        private Sprite _iconError;

        private static readonly Queue<ToastInfo> _toastQueue = new();
        private readonly Dictionary<string, Toast> _activeToastsByKey = new();

        private enum ToastType
        {
            General,
            Information,
            Success,
            Warning,
            Error,
        }

        private readonly struct ToastInfo
        {
            public readonly ToastType Type;
            public readonly string Text;
            public readonly Action OnClick;
            public readonly string ReplacementKey;

            public ToastInfo(ToastType type, string text, Action onClick, string replacementKey)
            {
                Type           = type;
                Text           = text;
                OnClick        = onClick;
                ReplacementKey = replacementKey;
            }
        }

        protected override void SingletonAwake()
        {
            base.SingletonAwake();
            EnsureTopmostCanvas();
        }

        private void Update()
        {
            if (LoadingScreen.IsActive)
            {
                return;
            }

            while (transform.childCount < MAX_TOAST_COUNT && _toastQueue.TryDequeue(out var toast))
            {
                ShowToast(toast);
            }
        }

        /// <summary>
        /// Adds a general message toast to the toast queue.
        /// </summary>
        /// <param name="text">Text of the toast.</param>
        /// <param name="onClick">Action to perform when the toast is clicked.</param>
        public static void ToastMessage(string text, Action onClick = null, string replacementKey = null)
            => AddToast(ToastType.General, text, onClick, replacementKey);

        /// <summary>
        /// Adds an information message toast to the toast queue.
        /// </summary>
        /// <param name="text">Text of the toast.</param>
        /// <param name="onClick">Action to perform when the toast is clicked.</param>
        public static void ToastInformation(string text, Action onClick = null, string replacementKey = null)
            => AddToast(ToastType.Information, text, onClick, replacementKey);

        /// <summary>
        /// Adds a success message toast to the toast queue.
        /// </summary>
        /// <param name="text">Text of the toast.</param>
        /// <param name="onClick">Action to perform when the toast is clicked.</param>
        public static void ToastSuccess(string text, Action onClick = null, string replacementKey = null)
            => AddToast(ToastType.Success, text, onClick, replacementKey);

        /// <summary>
        /// Adds a warning message toast to the toast queue.
        /// </summary>
        /// <param name="text">Text of the toast.</param>
        /// <param name="onClick">Action to perform when the toast is clicked.</param>
        public static void ToastWarning(string text, Action onClick = null, string replacementKey = null)
            => AddToast(ToastType.Warning, text, onClick, replacementKey);

        /// <summary>
        /// Adds an error message toast to the toast queue.
        /// </summary>
        /// <param name="text">Text of the toast.</param>
        /// <param name="onClick">Action to perform when the toast is clicked.</param>
        public static void ToastError(string text, Action onClick = null, string replacementKey = null)
            => AddToast(ToastType.Error, text, onClick, replacementKey);

        private static void AddToast(ToastType type, string text, Action onClick, string replacementKey)
        {
            if (!string.IsNullOrEmpty(replacementKey))
            {
                RemoveQueuedToasts(replacementKey);

                if (Instance != null && Instance.TryUpdateToast(replacementKey, type, text, onClick))
                {
                    return;
                }
            }

            _toastQueue.Enqueue(new ToastInfo(type, text, onClick, replacementKey));
        }

        private static void RemoveQueuedToasts(string replacementKey)
        {
            int count = _toastQueue.Count;
            for (int i = 0; i < count; i++)
            {
                var toast = _toastQueue.Dequeue();
                if (toast.ReplacementKey != replacementKey)
                {
                    _toastQueue.Enqueue(toast);
                }
            }
        }

        private void EnsureTopmostCanvas()
        {
            var canvas = GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = gameObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            canvas.overrideSorting = true;
            canvas.sortingOrder = _sortingOrder;

            if (GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();
        }

        private bool TryUpdateToast(string replacementKey, ToastType type, string body, Action onClick)
        {
            if (!_activeToastsByKey.TryGetValue(replacementKey, out var toast) || toast == null)
            {
                _activeToastsByKey.Remove(replacementKey);
                return false;
            }

            var (title, color, icon) = GetToastVisuals(type);
            toast.UpdateToast(title, body, icon, color, onClick);
            return true;
        }

        private void ShowToast(ToastInfo toastInfo)
        {
            var (title, color, icon) = GetToastVisuals(toastInfo.Type);

            var toast = Instantiate(_toastPrefab, transform);
            toast.Initialize(title, toastInfo.Text, icon, color, toastInfo.OnClick, OnToastDestroyed);

            if (!string.IsNullOrEmpty(toastInfo.ReplacementKey))
            {
                _activeToastsByKey[toastInfo.ReplacementKey] = toast;
            }

            void OnToastDestroyed(Toast destroyedToast)
            {
                if (!string.IsNullOrEmpty(toastInfo.ReplacementKey) &&
                    _activeToastsByKey.TryGetValue(toastInfo.ReplacementKey, out var activeToast) &&
                    activeToast == destroyedToast)
                {
                    _activeToastsByKey.Remove(toastInfo.ReplacementKey);
                }
            }
        }

        private (string title, Color color, Sprite icon) GetToastVisuals(ToastType type)
        {
            return type switch
            {
                ToastType.General     => ("General",     _generalColor,     _iconGeneral),
                ToastType.Information => ("Information", _informationColor, _iconInformation),
                ToastType.Success     => ("Success",     _successColor,     _iconSuccess),
                ToastType.Warning     => ("Warning",     _warningColor,     _iconWarning),
                ToastType.Error       => ("Error",       _errorColor,       _iconError),
                _ => throw new ArgumentException($"Invalid toast type {type}!")
            };
        }
    }
}
