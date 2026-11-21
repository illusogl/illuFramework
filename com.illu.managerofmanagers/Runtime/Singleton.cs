using System;

namespace ManagerOfManagers.Core
{
    public abstract class Singleton<T> : IManager where T : Singleton<T>
    {
        /// <summary>
        /// 创建 T 的延迟包装器，只有内部能用 用Lazy来保证只初始化一次
        /// </summary>
        private static Lazy<T> _lazy = Create();
        /// <summary>
        /// 构造 Lazy 包装器本身；此时并不创建 T 的实例。实例在首次读取 Instance 时才由工厂创建。
        /// 如果mgr没有无参构造函数则会报错
        /// </summary>
        private static Lazy<T> Create()
        {
            return new Lazy<T>(() =>
            {
                try
                {
                    return (T)Activator.CreateInstance(typeof(T),nonPublic : true);
                }
                catch(MissingMethodException e)
                {
                    throw new InvalidOperationException(
                         $"{typeof(T).Name} 需要一个无参构造函数（private 也可以）。", e);
                }
            },System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);
        }
        /// <summary>
        /// 全局实例入口 直接AudioManager.Instance就行
        /// </summary>
        public static T Instance => _lazy.Value;
        /// <summary>
        /// 判断实例是否已经构建
        /// </summary>
        public static bool Exists => _lazy.IsValueCreated;
        /// <summary>
        /// 重新创建实例的方法
        /// </summary>
        public static void ResetInstance()
        {
            if(_lazy.IsValueCreated) _lazy.Value.OnDispose();
            _lazy = Create();
        }
        protected Singleton() {}

        /// <summary>
        /// 驱动顺序 用于给ManagerHub管理调用顺序 升序调用Oninit 降序调用OnDispose
        /// </summary>
        public abstract int Order { get; }

        /// <summary>
        /// 销毁函数 让ManagerHub执行销毁管理器的方法
        /// </summary>
        public virtual void OnDispose(){}


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

