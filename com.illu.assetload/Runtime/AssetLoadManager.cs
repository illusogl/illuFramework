using System;
using System.Collections.Generic;
using ManagerOfManagers.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Illu.AssetLoad
{
    /// <summary>由 Hub 驱动的 Unity Resources 和场景加载入口。</summary>
    /// <remarks>调用方选择加载时机。管理器跟踪异步句柄、顺序批次和同步场景的完成状态。</remarks>
    public sealed class AssetLoadManager : Singleton<AssetLoadManager>
    {
        // 记录本管理器提交的异步操作，供同步切场景前检查在途工作。
        readonly List<AsyncOperation> _requests = new();

        // 由 OnInit 记录 Hub 所在的 Unity 主线程。0 表示尚未初始化或已经释放。
        int _mainThreadId;
        // 同步场景没有返回句柄，靠目标索引和加载模式匹配 sceneLoaded。-1 表示没有在等。
        int _syncSceneIndex = -1;
        LoadSceneMode _syncSceneMode;
        // 这份引用用来阻止重复提交场景请求，完成跟踪同时由 _requests 负责。
        AsyncOperation _asyncScene;
        // 同一时刻推进一个批次，依次提交它的资源请求。
        ResourceLoadBatch _batch;

        /// <summary>实例由 Singleton 基类创建，Hub 负责初始化与释放。</summary>
        private AssetLoadManager() { }

        /// <summary>在 Demo 中先于会读取资源的 AudioManager 初始化，确保加载入口已经就绪。</summary>
        public override int Order => 5;
        /// <summary>记录 Hub 所在的主线程，并订阅 sceneLoaded 来确认同步场景加载完成。</summary>
        /// <remarks>同一次初始化期间重复进入时直接返回，以免重复订阅事件。</remarks>
        public override void OnInit()
        {
            if (_mainThreadId != 0) return;

            _mainThreadId = Environment.CurrentManagedThreadId;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        /// <summary>在当前调用中同步读取一项 Resources 资源，完成后直接返回该资源。</summary>
        /// <typeparam name="T">要读取的 Unity 资源类型，例如 AudioLibrary、GameObject 或 AudioClip。</typeparam>
        /// <param name="path">相对 Resources 目录的资源路径。子目录用正斜杠，路径省略文件扩展名。</param>
        /// <returns>获取到符合类型的资源时返回资源。请求被拒绝或 Unity 找不到资源时返回 null。</returns>
        /// <remarks>同步读取会占用当前调用帧。需要界面继续更新时，可以用异步入口等待请求。</remarks>
        public T LoadResource<T>(string path) where T : Object
        {
            if (!EnsureReady() || !ValidatePath(path)) return null;

            var asset = Resources.Load<T>(path);
            if (asset == null)
                Debug.LogError($"[AssetLoadManager] 找不到资源 {path} ({typeof(T).Name})。");

            return asset;
        }

        /// <summary>把一项 Resources 加载提交给 Unity，同时登记句柄供同步切场景时检查。</summary>
        /// <typeparam name="T">要读取的 Unity 资源类型，例如 AudioLibrary、GameObject 或 AudioClip。</typeparam>
        /// <param name="path">相对 Resources 目录的资源路径。子目录用正斜杠，路径省略文件扩展名。</param>
        /// <returns>请求被接受时返回 Unity 的 ResourceRequest。调用方可等待 isDone，完成后用 asset as T 取得资源。</returns>
        /// <remarks>返回 null 表示加载未提交。拿到句柄后，完成时的 asset 仍可能为 null，例如路径下没有匹配类型的资源。</remarks>
        public ResourceRequest LoadResourceAsync<T>(string path) where T : Object
        {
            if (!EnsureReady() || !ValidatePath(path)) return null;

            var request = Resources.LoadAsync<T>(path);
            _requests.Add(request);
            return request;
        }

        /// <summary>登记一批由 Hub 逐帧推进的资源。每项结束后才向 Unity 提交下一项。</summary>
        /// <typeparam name="T">要读取的 Unity 资源类型，例如 AudioLibrary、GameObject 或 AudioClip。</typeparam>
        /// <param name="paths">至少包含一个 Resources 路径。提交时完整校验并复制，加载及结果按原顺序处理。</param>
        /// <returns>返回可在协程中等待的批次。管理器未就绪、参数无效或上一批仍在进行时返回 null。</returns>
        /// <remarks>加载失败的条目会留下 null。其位置仍对应提交时的原始索引，剩余条目会继续加载。</remarks>
        public ResourceLoadBatch LoadResourceBatchAsync<T>(IReadOnlyList<string> paths) where T : Object
        {
            if (!EnsureReady()) return null;

            if (_batch != null && !_batch.IsDone)
            {
                Debug.LogError("[AssetLoadManager] 当前批次还在进行。");
                return null;
            }

            if (paths == null || paths.Count == 0)
            {
                Debug.LogError("[AssetLoadManager] 批次需要至少一个资源路径。");
                return null;
            }

            var snapshot = new string[paths.Count];
            for (int i = 0; i < snapshot.Length; i++)
            {
                var path = paths[i];
                if (!ValidatePath(path)) return null;

                snapshot[i] = path;
            }

            _batch = new ResourceLoadBatch(snapshot, typeof(T));
            return _batch;
        }

        /// <summary>通过 Unity 的同步入口提交场景请求，并等待匹配的 sceneLoaded 确认就绪。</summary>
        /// <param name="buildIndex">当前构建配置中已启用的场景索引。</param>
        /// <param name="mode">Single 替换当前场景，Additive 把目标场景叠加到现有场景。</param>
        /// <returns>Unity 的场景加载调用已发出时返回 true。管理器未就绪、参数无效或仍有加载任务进行时返回 false。</returns>
        /// <remarks>同步切场景会先检查管理器跟踪的所有任务。这样能避免 Unity 的同步场景入口强制完成先前的异步操作。</remarks>
        public bool LoadScene(int buildIndex, LoadSceneMode mode = LoadSceneMode.Single)
        {
            if (!CanLoadScene(buildIndex, mode)) return false;

            if (HasPendingLoads())
            {
                Debug.LogError("[AssetLoadManager] 同步场景加载前要先等在途加载完成。");
                return false;
            }

            // 提交前记录目标，便于 sceneLoaded 识别它。调用抛异常时撤销这条等待记录。
            _syncSceneIndex = buildIndex;
            _syncSceneMode = mode;
            try
            {
                SceneManager.LoadScene(buildIndex, mode);
                return true;
            }
            catch
            {
                _syncSceneIndex = -1;
                throw;
            }
        }

        /// <summary>把场景交给 Unity 异步加载。默认允许 Unity 在资源就绪后激活场景。</summary>
        /// <param name="buildIndex">当前构建配置中已启用的场景索引。</param>
        /// <param name="mode">Single 替换当前场景，Additive 把目标场景叠加到现有场景。</param>
        /// <returns>返回可读取 progress 和 isDone 的场景操作。请求未被接受时返回 null。</returns>
        /// <remarks>场景激活完成后 isDone 才为 true。调用方可以通过该句柄决定什么时候结束加载界面。</remarks>
        public AsyncOperation LoadSceneAsync(int buildIndex, LoadSceneMode mode = LoadSceneMode.Single)
        {
            if (!CanLoadScene(buildIndex, mode)) return null;

            _asyncScene = SceneManager.LoadSceneAsync(buildIndex, mode);
            if (_asyncScene == null)
            {
                Debug.LogError($"[AssetLoadManager] Unity 未接受场景索引 {buildIndex} 的异步请求。");
                return null;
            }

            _requests.Add(_asyncScene);
            return _asyncScene;
        }

        /// <summary>由 Hub 每帧清理已完成的 Unity 句柄，并检查批次的当前项能否结算和推进下一项。</summary>
        /// <param name="dt">Hub 传入的帧时间。加载进度以 Unity 句柄为准。</param>
        public override void OnTick(float dt)
        {
            _requests.RemoveAll(static request => request.isDone);

            if (_asyncScene != null && _asyncScene.isDone)
                _asyncScene = null;

            if (_batch == null) return;

            _batch.Advance();
            if (_batch.IsDone)
                _batch = null;
        }

        /// <summary>注销场景事件，停止批次继续提交任务，清理句柄和初始化状态。</summary>
        /// <remarks>这会终止管理器自己的等待和跟踪。已送交 Unity 的底层异步操作由 Unity 完成。</remarks>
        public override void OnDispose()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _batch?.StopScheduling();
            _batch = null;
            _asyncScene = null;
            _requests.Clear();
            _syncSceneIndex = -1;
            _mainThreadId = 0;
        }

        /// <summary>匹配同步场景的完成事件。收到对应场景索引和模式后，释放这个场景请求占用的位置。</summary>
        /// <param name="scene">刚完成加载的场景。</param>
        /// <param name="mode">Unity 在回调中报告的实际加载方式。</param>
        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.buildIndex == _syncSceneIndex && mode == _syncSceneMode)
                _syncSceneIndex = -1;
        }

        /// <summary>确认当前调用来自 Unity 主线程，同时检查是否已有场景在加载，以及目标场景能否从当前构建配置加载。</summary>
        /// <param name="buildIndex">当前构建配置中已启用的场景索引。</param>
        /// <param name="mode">要传给 Unity 的场景加载方式。</param>
        /// <returns>通过场景入口的共有校验时返回 true。同步入口之后还会检查其他加载任务。</returns>
        bool CanLoadScene(int buildIndex, LoadSceneMode mode)
        {
            if (!EnsureReady()) return false;

            if (_syncSceneIndex >= 0 || (_asyncScene != null && !_asyncScene.isDone))
            {
                Debug.LogError("[AssetLoadManager] 正在加载另一个场景。");
                return false;
            }

            if (mode != LoadSceneMode.Single && mode != LoadSceneMode.Additive)
            {
                Debug.LogError($"[AssetLoadManager] 无效的场景加载方式 {mode}。");
                return false;
            }

            if (buildIndex < 0 || !Application.CanStreamedLevelBeLoaded(buildIndex))
            {
                Debug.LogError($"[AssetLoadManager] 当前构建配置不能加载场景索引 {buildIndex}。");
                return false;
            }

            return true;
        }

        /// <summary>同步切场景前，核对管理器已接收的所有加载任务。</summary>
        /// <returns>存在尚未完成的场景、批次或异步资源请求时返回 true。</returns>
        bool HasPendingLoads()
        {
            if (_syncSceneIndex >= 0 || (_batch != null && !_batch.IsDone))
                return true;

            foreach (var request in _requests)
            {
                if (!request.isDone)
                    return true;
            }

            return false;
        }

        /// <summary>将当前线程与 Hub 初始化时记录的主线程比对。</summary>
        /// <returns>管理器已经初始化并且调用来自同一主线程时返回 true。</returns>
        bool EnsureReady()
        {
            if (_mainThreadId != 0 && _mainThreadId == Environment.CurrentManagedThreadId)
                return true;

            Debug.LogError("[AssetLoadManager] 请在 Hub 初始化后从 Unity 主线程调用。");
            return false;
        }

        /// <summary>检查传给 Unity 的路径有可用内容。</summary>
        /// <param name="path">调用方提供的资源路径。</param>
        /// <returns>路径含有非空白字符时返回 true。</returns>
        static bool ValidatePath(string path)
        {
            if (!string.IsNullOrWhiteSpace(path)) return true;

            Debug.LogError("[AssetLoadManager] Resources 路径不能为空。");
            return false;
        }
    }
}
