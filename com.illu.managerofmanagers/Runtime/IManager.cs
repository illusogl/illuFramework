namespace ManagerOfManagers.Core
{
    /// <summary>
    /// 管理器通用接口
    /// </summary>
    public interface IManager
    {
        /// <summary>
        /// 驱动顺序 用于给ManagerHub管理调用顺序 升序调用Oninit 降序调用OnDispose
        /// </summary>
        int Order { get; }

        /// <summary>
        /// 初始化函数 让ManagerHub执行初始化管理器的方法
        /// </summary>
        void OnInit();

        /// <summary>
        /// 模拟Update 每帧调用管理器 让Managerhub每隔一定的间隔时间（dt）就执行的函数
        /// </summary>
        /// <param name="dt">间隔时间 单位ms</param>
        void OnTick(float dt);

        /// <summary>
        /// 模拟LateUpdate 每帧调用管理器 让Managerhub每隔一定的间隔时间（dt）就执行的函数
        /// 执行一定是放在Update后执行
        /// </summary>
        /// <param name="dt">间隔时间</param>
        void OnLateTick(float dt);

        /// <summary>
        /// 销毁函数 让ManagerHub执行销毁管理器的方法
        /// </summary>
        void OnDispose();
    }
}

