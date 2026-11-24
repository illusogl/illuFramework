using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Illu.AssetLoad
{
    /// <summary>由 AssetLoadManager 每帧推进的顺序加载批次，同时提供进度和按输入顺序排列的结果。</summary>
    /// <remarks>可以在协程中直接等待。同一批次一次只发出一项 Unity 资源请求。</remarks>
    public sealed class ResourceLoadBatch : CustomYieldInstruction
    {
        // 路径由管理器校验并复制，和 _assets 一一对应。
        readonly string[] _paths;
        readonly Type _assetType;
        readonly Object[] _assets;
        ResourceRequest _request;
        // 已结算项数也是当前要处理的索引。查询失败的项同样会结算。
        int _completed;

        /// <summary>为有效路径的快照分配固定长度的结果槽。</summary>
        /// <param name="paths">管理器复制后的非空路径数组，顺序与结果数组一致。</param>
        /// <param name="assetType">发给 Unity 的资源类型，整批都按此类型查找。</param>
        internal ResourceLoadBatch(string[] paths, Type assetType)
        {
            _paths = paths;
            _assetType = assetType;
            _assets = new Object[paths.Length];
            Assets = Array.AsReadOnly(_assets);
        }

        /// <summary>按输入索引提供结果的只读视图。批次推进时管理器逐项填入，未完成项和失败项可以都是 null。</summary>
        /// <remarks>等待批次结算后配合 FailureCount 或 IsCanceled 判断整批结果。</remarks>
        public IReadOnlyList<Object> Assets { get; }
        /// <summary>已经结算且未取到资源的条目数。尚未处理的条目不会增加这个计数。</summary>
        public int FailureCount { get; private set; }
        /// <summary>管理器释放后停止推进的批次会标记为 true，已填入的结果槽仍可读取。</summary>
        public bool IsCanceled { get; private set; }
        /// <summary>全部条目均已结算或管理器已取消批次时为 true。是否成功可结合 IsCanceled 和 FailureCount 判断。</summary>
        public bool IsDone => IsCanceled || _completed == _paths.Length;
        /// <summary>按条目等权计算进度。已结算项数加当前请求的进度，再除以总条目数。</summary>
        /// <remarks>自然结算到最后一项时为 1。取消时保留已结算比例，各文件的大小不影响条目权重。</remarks>
        public float Progress => (_completed + (_request?.progress ?? 0f)) / _paths.Length;
        /// <summary>供 Unity 的协程检查是否继续等待。批次自然结束或取消都能让等待中的协程继续。</summary>
        public override bool keepWaiting => !IsDone;

        /// <summary>由 Hub 间接调用。当前请求完成后先填结果，再在还有下一个索引时提交下一项。</summary>
        internal void Advance()
        {
            if (_request != null)
            {
                if (!_request.isDone) return;

                var asset = _request.asset;
                _assets[_completed] = asset;
                if (asset == null)
                {
                    FailureCount++;
                    Debug.LogError($"[AssetLoadManager] 批次中找不到资源 {_paths[_completed]} ({_assetType.Name})。");
                }

                // 缺失项也占自己的结果槽。无论查找是否成功，都推进到下一项。
                _completed++;
                _request = null;
            }

            if (!IsDone)
                _request = Resources.LoadAsync(_paths[_completed], _assetType);
        }

        /// <summary>管理器释放时终止本批次的等待，并停止提交剩余条目。</summary>
        /// <remarks>已提交给 Unity 的请求继续由 Unity 处理。已结算的结果仍留在批次中。</remarks>
        internal void StopScheduling()
        {
            if (IsDone) return;

            IsCanceled = true;
            _request = null;
        }
    }
}
