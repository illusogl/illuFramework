using System;
using System.Collections.Generic;
using ManagerOfManagers.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using RebindingOperation = UnityEngine.InputSystem.InputActionRebindingExtensions.RebindingOperation;

namespace Illu.Input
{
    /// <summary>GameHub 驱动的单人玩法输入。玩法资产应由此实例独占管理。</summary>
    public sealed class InputManager : IManager
    {
        /// <summary>一条可显示、可改绑的绑定，供设置界面使用。</summary>
        public readonly struct BindingSlot
        {
            /// <summary>绑定在动作资产中的标识，设置界面用它提交改绑和还原请求。</summary>
            public Guid Id { get; }
            /// <summary>组合绑定的分量名称，例如 up。普通绑定的值为 null。</summary>
            public string PartName { get; }
            /// <summary>Unity 根据当前有效绑定生成的键名。</summary>
            public string DisplayName { get; }

            /// <summary>从 Unity 的绑定提取标识和分量名，再附上设置界面显示的键名。</summary>
            internal BindingSlot(InputBinding binding, string displayName)
            {
                Id = binding.id;
                PartName = binding.isPartOfComposite ? binding.name : null;
                DisplayName = displayName;
            }
        }

        readonly InputActionAsset _asset;
        readonly InputActionMap _gameplay;
        readonly List<Subscription> _subscriptions = new List<Subscription>();
        RebindingOperation _rebind;
        bool _initialized;
        // 回调可以在派发途中暂停或换方案。版本变化时结束本轮的派发。
        int _inputRevision;

        /// <summary>供 ManagerHub 排定初始化、更新和退出顺序。</summary>
        public int Order { get; }
        /// <summary>当前玩法动作组是否实际开启。</summary>
        public bool GameplayEnabled => _initialized && _gameplay.enabled;
        /// <summary>是否有一项交互式改绑正在等待玩家输入。</summary>
        public bool IsRebinding => _rebind != null;
        /// <summary>当前方案名。启动后由游戏层设定。</summary>
        public string CurrentControlScheme { get; private set; }

        /// <summary>接收由 GameHub 配置的专用资产，定位要管理的玩法动作组。</summary>
        public InputManager(InputActionAsset asset, string mapName = "Gameplay", int order = 16)
        {
            if (asset == null)
                throw new ArgumentNullException(nameof(asset));

            // 全项目动作由 Unity 管理启停，本实例需要独立的玩法资产。
            if (asset == InputSystem.actions)
                throw new ArgumentException("Gameplay requires a dedicated action asset.", nameof(asset));

            _asset = asset;
            _gameplay = asset.FindActionMap(mapName, throwIfNotFound: true);
            Order = order;
        }

        /// <summary>准备动作组，让游戏层在首帧输入前恢复玩家设置。</summary>
        void IManager.OnInit()
        {
            if (_initialized)
                return;

            _gameplay.Disable();
            // 资产直接复用。初始化时清理上一轮留下的运行时方案和覆盖。
            _gameplay.bindingMask = null;
            _gameplay.RemoveAllBindingOverrides();
            CurrentControlScheme = null;
            _initialized = true;
        }

        /// <summary>按注册顺序派发待处理的按钮输入，并采样连续值。</summary>
        void IManager.OnTick(float dt)
        {
            // 订阅者可在回调里解绑。统一在派发前移除已解绑的条目。
            _subscriptions.RemoveAll(subscription => subscription.IsDisposed);
            if (!GameplayEnabled)
                return;

            int revision = _inputRevision;
            // 回调中新增加的订阅从下一次 Tick 开始派发。
            int count = _subscriptions.Count;
            for (int i = 0; i < count && revision == _inputRevision; i++)
            {
                try
                {
                    _subscriptions[i].Tick();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        /// <summary>保留 ManagerHub 的晚更新入口。输入派发在 OnTick 完成。</summary>
        void IManager.OnLateTick(float dt) { }

        /// <summary>在 Hub 退出时停用玩法输入，结束改绑并解除订阅。</summary>
        void IManager.OnDispose()
        {
            // 先撤销开启条件，随后取消改绑时也会保持关闭。
            _initialized = false;
            StopGameplay();
            CancelRebind();

            foreach (var subscription in _subscriptions)
                subscription.Dispose();

            _subscriptions.Clear();
        }

        /// <summary>直接启停玩法动作组。正在改绑时保持停用。</summary>
        public void SetGameplayEnabled(bool enabled)
        {
            if (!_initialized || _gameplay.enabled == enabled)
                return;

            if (!enabled)
                StopGameplay();
            else if (!IsRebinding)
                _gameplay.Enable();
        }

        /// <summary>订阅按钮动作的触发。返回值交给订阅者在离开时调用 Dispose。</summary>
        public IDisposable SubscribeButton(InputActionReference reference, Action handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            var action = Resolve(reference);
            if (action.type != InputActionType.Button)
                throw new ArgumentException("The action must have type Button.", nameof(reference));

            var subscription = new ButtonSubscription(action, handler);
            _subscriptions.Add(subscription);
            return subscription;
        }

        /// <summary>订阅连续动作的值变化。T 需与动作值的实际类型一致，例如 Vector2。</summary>
        public IDisposable SubscribeValue<T>(InputActionReference reference, Action<T> handler) where T : struct
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            var action = Resolve(reference);
            if (action.type == InputActionType.Button)
                throw new ArgumentException("Use SubscribeButton for button actions.", nameof(reference));

            var subscription = new ValueSubscription<T>(action, handler);
            _subscriptions.Add(subscription);
            return subscription;
        }

        /// <summary>切换到指定方案的绑定组。方案无效或改绑进行中时返回 false。</summary>
        public bool SetControlScheme(string schemeName)
        {
            if (!_initialized || IsRebinding || string.IsNullOrEmpty(schemeName))
                return false;

            var scheme = _asset.FindControlScheme(schemeName);
            if (!scheme.HasValue)
                return false;

            if (CurrentControlScheme == scheme.Value.name)
                return true;

            // Unity 更新绑定组后，清理订阅中记录的旧输入。
            _gameplay.bindingMask = InputBinding.MaskByGroup(scheme.Value.bindingGroup);
            CurrentControlScheme = scheme.Value.name;
            ResetSubscriptions();
            return true;
        }

        /// <summary>列出动作在指定方案下可供界面逐条设置的绑定。</summary>
        public IReadOnlyList<BindingSlot> GetBindings(InputActionReference reference, string schemeName)
        {
            var action = Resolve(reference);
            var slots = new List<BindingSlot>();
            if (string.IsNullOrEmpty(schemeName))
                return slots;

            var scheme = _asset.FindControlScheme(schemeName);
            if (!scheme.HasValue)
                return slots;

            var mask = InputBinding.MaskByGroup(scheme.Value.bindingGroup);
            for (int i = 0; i < action.bindings.Count; i++)
            {
                var binding = action.bindings[i];
                // 组合父项描述合成方式。设置界面只列可独立改绑的分量和普通绑定。
                if (!binding.isComposite && mask.Matches(binding))
                    slots.Add(new BindingSlot(binding, action.GetBindingDisplayString(i)));
            }

            return slots;
        }

        /// <summary>在玩法停用时开始改绑。回调的 true 表示已应用，false 表示已取消。</summary>
        public bool BeginRebind(InputActionReference reference, Guid bindingId, Action<bool> finished)
        {
            // 游戏流程先暂停玩法，Unity 才能开始监听这条绑定的新输入。
            if (!_initialized || GameplayEnabled || IsRebinding)
                return false;

            var action = Resolve(reference);
            int index = FindBinding(action, bindingId);
            if (index < 0)
                return false;

            try
            {
                _rebind = action.PerformInteractiveRebinding(index);
                _rebind.WithCancelingThrough("<Keyboard>/escape")
                    .OnComplete(operation => FinishRebind(operation, finished, true))
                    .OnCancel(operation => FinishRebind(operation, finished, false))
                    .Start();
                return true;
            }
            catch
            {
                // 启动失败时释放已创建的操作，玩法继续保持停用。
                _rebind?.Dispose();
                _rebind = null;
                throw;
            }
        }

        /// <summary>取消当前改绑，并让 Unity 走统一的取消回调。</summary>
        public void CancelRebind()
        {
            _rebind?.Cancel();
        }

        /// <summary>移除单条绑定的玩家覆盖，恢复资产中的默认绑定。</summary>
        public bool ResetBinding(InputActionReference reference, Guid bindingId)
        {
            if (!_initialized || IsRebinding)
                return false;

            var action = Resolve(reference);
            int index = FindBinding(action, bindingId);
            if (index < 0)
                return false;

            action.RemoveBindingOverride(index);
            ResetSubscriptions();
            return true;
        }

        /// <summary>导出玩法动作组的绑定覆盖 JSON，交给游戏层保存。</summary>
        public string SaveBindingOverrides() => _gameplay.SaveBindingOverridesAsJson();

        /// <summary>恢复绑定覆盖。空内容表示使用默认绑定。</summary>
        public bool LoadBindingOverrides(string json)
        {
            if (!_initialized || IsRebinding)
                return false;

            // 读档前保留当前覆盖，格式错误时可以恢复。
            string previous = SaveBindingOverrides();
            try
            {
                if (string.IsNullOrEmpty(json))
                    _gameplay.RemoveAllBindingOverrides();
                else
                    _gameplay.LoadBindingOverridesFromJson(json);

                return true;
            }
            catch (ArgumentException exception)
            {
                _gameplay.LoadBindingOverridesFromJson(previous);
                Debug.LogWarning($"[InputManager] Could not load binding overrides. {exception.Message}");
                return false;
            }
            finally
            {
                ResetSubscriptions();
            }
        }

        /// <summary>解析 Inspector 配置的动作引用，确认动作属于本实例管理的动作组。</summary>
        InputAction Resolve(InputActionReference reference)
        {
            var action = reference == null ? null : reference.action;
            if (action == null || action.actionMap != _gameplay)
                throw new ArgumentException("The reference must point to a managed gameplay action.", nameof(reference));

            return action;
        }

        /// <summary>用绑定标识查找 Unity 当前使用的索引。找不到可修改的绑定时返回 -1。</summary>
        static int FindBinding(InputAction action, Guid bindingId)
        {
            for (int i = 0; i < action.bindings.Count; i++)
            {
                var binding = action.bindings[i];
                if (binding.id == bindingId && !binding.isComposite)
                    return i;
            }

            return -1;
        }

        /// <summary>停用玩法动作组，并重置当前的订阅状态。</summary>
        void StopGameplay()
        {
            _gameplay.Disable();
            ResetSubscriptions();
        }

        /// <summary>清空待派发的按钮次数，让已输出的连续值回到默认值。</summary>
        void ResetSubscriptions()
        {
            // 让当前的派发循环结束，避免把暂停前或改绑定前的输入继续传下去。
            _inputRevision++;
            int count = _subscriptions.Count;
            for (int i = 0; i < count; i++)
            {
                try
                {
                    _subscriptions[i].Reset();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        /// <summary>释放改绑操作并通知结果。玩法由游戏流程显式恢复。</summary>
        void FinishRebind(RebindingOperation operation, Action<bool> finished, bool applied)
        {
            _rebind = null;
            // 改绑操作持有 Unity 的资源。完成和取消都通过这里释放。
            operation.Dispose();
            finished?.Invoke(applied);
        }
        #region 订阅Subscription结构体
        /// <summary>统一按钮和连续值订阅的派发、停用重置与解绑接口。</summary>
        abstract class Subscription : IDisposable
        {
            public bool IsDisposed { get; protected set; }
            /// <summary>在 Hub 的 Tick 中派发本订阅的输入。</summary>
            public abstract void Tick();
            /// <summary>玩法动作组停用时清理本订阅的输入状态。</summary>
            public abstract void Reset();
            /// <summary>解绑并将订阅标记为可移出列表。</summary>
            public abstract void Dispose();
        }

        /// <summary>缓存按钮动作的触发次数，由 Hub 统一派发。</summary>
        sealed class ButtonSubscription : Subscription
        {
            readonly InputAction _action;
            readonly Action _handler;
            int _pending;

            /// <summary>注册 Unity 动作的 performed 回调，并保存游戏侧处理方法。</summary>
            public ButtonSubscription(InputAction action, Action handler)
            {
                _action = action;
                _handler = handler;
                _action.performed += OnPerformed;
            }

            /// <summary>记录一次动作触发，业务回调在随后由 Tick 调用。</summary>
            void OnPerformed(InputAction.CallbackContext context) => _pending++;

            /// <summary>逐次派发累计的按钮触发。</summary>
            public override void Tick()
            {
                // 保留一个 Tick 之前收到的多次触发。回调暂停时 Reset 会立刻清空剩余次数。
                while (!IsDisposed && _pending > 0)
                {
                    _pending--;
                    _handler();
                }
            }

            /// <summary>清空待派发的按钮触发次数。</summary>
            public override void Reset() => _pending = 0;

            /// <summary>解除 Unity 的 performed 回调，并丢弃未派发的触发。</summary>
            public override void Dispose()
            {
                if (IsDisposed)
                    return;

                IsDisposed = true;
                _action.performed -= OnPerformed;
                Reset();
            }
        }

        /// <summary>读取连续值，只在值发生变化时发布。</summary>
        sealed class ValueSubscription<T> : Subscription where T : struct
        {
            readonly InputAction _action;
            readonly Action<T> _handler;
            T _lastValue;

            /// <summary>保存待采样的动作和接收连续值的业务回调。</summary>
            public ValueSubscription(InputAction action, Action<T> handler)
            {
                _action = action;
                _handler = handler;
            }

            /// <summary>在当前 Tick 读取动作值，必要时发布变化。</summary>
            public override void Tick()
            {
                if (!IsDisposed)
                    Publish(_action.ReadValue<T>());
            }

            /// <summary>发布该类型的默认值，例如 Vector2.zero。</summary>
            public override void Reset()
            {
                if (!IsDisposed)
                    Publish(default);
            }

            /// <summary>停止采样和重置通知，将订阅标记为可移出列表。</summary>
            public override void Dispose() => IsDisposed = true;

            /// <summary>更新上一次输出的值，并向订阅者通知变化。</summary>
            void Publish(T value)
            {
                if (EqualityComparer<T>.Default.Equals(_lastValue, value))
                    return;

                // 先记录已发送的值，再调用业务。回调中触发暂停时可以正确重置这个值。
                _lastValue = value;
                _handler(value);
            }
        }
        #endregion
    }
}