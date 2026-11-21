using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;
namespace ManagerOfManagers.Core
{
    [DefaultExecutionOrder(-1000)]
    public abstract class ManagerHub : MonoSingleton<ManagerHub>
    {
        /// <summary>
        /// 用来存储manager列表的List
        /// </summary>
        private readonly List<IManager> _managers = new List<IManager>(16);

        /// <summary>
        /// managers的外部只读 注册表 _readonlyManager为了防止外部Clear
        /// </summary>
        ReadOnlyCollection<IManager> _readonlyManager;
        public IReadOnlyList<IManager> Managers => _readonlyManager ??= _managers.AsReadOnly();

        /// <summary>
        /// 给外部重写要挂什么样的Manager的函数
        /// public sealed class GameHub : ManagerHub
        /// {
        ///     protected override void RegisterManagers()
        ///     {
        ///         Register(AudioManager.Instance);
        ///         Register(vInputManager.Instance);
        ///     }
        /// }
        /// </summary>
        protected abstract void RegisterManagers();

        /// <summary>
        /// 排序方法
        /// </summary>
        static int CompareOrder(IManager a, IManager b) => a.Order.CompareTo(b.Order);

        #region 状态情况
        /// <summary>
        /// 是否处于Tick/LateTick/Dispose中,此时禁止该表
        /// </summary>
        bool _iterating;

        /// <summary>
        /// 是否在Boot中 防止重复进Boot
        /// </summary>
        bool _booting;

        /// <summary>
        /// 为true时表示Boot完成 可以进行Update
        /// </summary>
        bool _running;

        /// <summary>
        /// 检查start逻辑有没有正确执行
        /// </summary>
        bool _warnedNotBooted;

        #endregion

        protected override void OnSingletonAwake()
        {
            DontDestroyOnLoad(gameObject);
        }

        protected void Register(IManager manager)
        {
            if(manager == null)
            {
                Debug.LogError($"[{GetType().Name}] 正在尝试注册空manager");
                return;
            }
            if(_iterating)
            {
                Debug.LogError($"[{GetType().Name}]尝试在Tick/LateTick/Dispose时候注册[{manager.GetType().Name}]");
                return;
            }
            if(_managers.Contains(manager))
            {
                return;
            }
            foreach(var m in _managers)
            {
                if(m.Order == manager.Order)
                {
                    Debug.LogError($"[{GetType().Name}] [{manager.GetType().Name}]的Order和列表中[{m.GetType().Name}]的Order冲突");
                    return;
                }
            }
            _managers.Add(manager);

        }

        protected virtual void Start()
        {
            //完成_manager中所有的资源的初始化

            if (_running || _booting) return;

            _booting = true;
            try
            {
                //给外部Awake中重写 负责把所有需要的Manager写进去
                RegisterManagers();
                _managers.Sort(CompareOrder);
                // 注册只发生在 RegisterManagers；此处按 Order 升序初始化。
                for (int i = 0; i < _managers.Count; i++)
                    _managers[i].OnInit();              
            }
            finally
            {
                _booting = false;
            }

            _running = true;
        }
        void Update()
        {
            if (!_running)
            {
                WarnIfNotBooted();
                return;
            }
            Tick(Time.deltaTime, late: false);
        }

        void LateUpdate()
        {
            if (!_running) return;

            Tick(Time.deltaTime, late: true);
        }

        /// <summary>
        /// 在Update中不停更新的函数 顺序调用_manager中的Update和lateUpdate
        /// </summary>
        /// <param name="dt">间隔时间</param>
        /// <param name="late">判断是Update还是lateUpdate</param>
        void Tick(float dt, bool late)
        {
            _iterating = true;
            try
            {
                // 注册表在本帧内不会变（改表已被 _iterating 拦住），索引遍历安全
                for (int i = 0; i < _managers.Count; i++)
                {
                    if (late) _managers[i].OnLateTick(dt);
                    else _managers[i].OnTick(dt);
                }
            }
            finally
            {
                _iterating = false;
            }
        }

        /// <summary>
        /// 销毁函数的重写
        /// </summary>
        protected override void OnDestroy()
        {
            _running = false;

            // 逆序释放：后初始化的先销毁，避免依赖方先死 如果已经被销毁过则跳过
            _iterating = true;
            try
            {
                for (int i = _managers.Count - 1; i >= 0; i--)
                {
                    if (_managers[i] is Object o && o == null) continue;
                    _managers[i].OnDispose();
                }
                   
            }
            finally
            {
                _iterating = false;
                _managers.Clear();
            }
            // 清空 MonoSingleton 的静态引用
            base.OnDestroy(); 
        }
        void WarnIfNotBooted()
        {
            if (_warnedNotBooted) return;
            _warnedNotBooted = true;

            Debug.LogError(
                $"[{GetType().Name}] 已经进入 Update 但没有完成初始化。" +
                "通常是子类覆写了 Start() 却没有调用 base.Start()。");
        }

        /// <summary>Hub 不进入自己的注册表，此值仅用于满足基类抽象成员。</summary>
        public override int Order => 0;
    }
}