using UnityEngine;
namespace ManagerOfManagers.Core
{
    [DisallowMultipleComponent]
    public abstract class MonoSingleton<T> : MonoBehaviour , IManager where T : MonoSingleton<T>
    {
        private static T _instance;
        public static T Instance => _instance;
        public static bool Exists => _instance != null;

        /// <summary>
        /// 保证在场景中找到该组件的方法
        /// </summary>
        /// <param name="goName">放单例脚本的对象名</param>
        /// <returns></returns>
        public static T Ensure(string goName = null)
        {
            if (_instance != null) return _instance;

            _instance = FindAnyObjectByType<T>(FindObjectsInactive.Include);
            if (_instance != null) return _instance;

            var go = new GameObject(string.IsNullOrEmpty(goName) ? typeof(T).Name : goName);
            return go.AddComponent<T>();
        }
        protected virtual void Awake()
        {
            if(_instance != null && _instance != this)
            {
                Debug.LogError($"[{typeof(T).Name}] 场景中存在多个实例，已移除 {name} 上的本个重复组件。", this);
                Destroy(this);
                //Destroy 是延迟到帧末才真正执行 必须return
                return;
            }
            _instance = (T)this;
            OnSingletonAwake();
        }
        protected virtual void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        /// <summary>
        /// 是否处于激活状态。失活只改变这个标志，不会销毁组件、也不会从注册表移除。
        /// </summary>
        public bool IsActive { get; private set; } = true;

        /// <summary>
        /// 激活 / 失活（幂等）。只影响自己，不传播给其它 Manager。
        /// 子类在 OnTick 首行检查 IsActive，失活期间就不会执行逻辑。
        /// </summary>
        public void SetActive(bool active)
        {
            if (IsActive == active) return;
            IsActive = active;
            if (active) OnActivate();
            else OnDeactivate();
        }

        /// <summary>由失活切换为激活时调用一次。</summary>
        protected virtual void OnActivate() { }

        /// <summary>由激活切换为失活时调用一次（可来回多次，不会重新 OnInit）。</summary>
        protected virtual void OnDeactivate() { }

        /// <summary>
        /// 驱动顺序 用于给ManagerHub管理调用顺序 升序调用Oninit 降序调用OnDispose
        /// </summary>
        public abstract int Order { get; }

        /// <summary>
        /// 销毁函数 让ManagerHub执行销毁管理器的方法
        /// </summary>
        public virtual void OnDispose(){}

        /// <summary>
        /// Mono单例模式独有的 在Awake时候调用的方法 由于纯C#单例没这种实际 所以是Mono独有
        /// </summary>
        protected virtual void OnSingletonAwake() { }

        /// <summary>
        /// 初始化函数 让ManagerHub执行初始化管理器的方法
        /// </summary>
        public virtual void OnInit(){}

        /// <summary>
        /// 模拟LateUpdate 每帧调用管理器 让Managerhub每隔一定的间隔时间（dt）就执行的函数
        /// 执行一定是放在Update后执行
        /// </summary>
        /// <param name="dt">间隔时间</param>
        public virtual void OnLateTick(float dt){}
        
        /// <summary>
        /// 模拟Update 每帧调用管理器 让Managerhub每隔一定的间隔时间（dt）就执行的函数
        /// </summary>
        /// <param name="dt">间隔时间 单位ms</param>
        public virtual void OnTick(float dt){}
    }
}

