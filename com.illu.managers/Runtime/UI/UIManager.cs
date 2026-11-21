using System.Collections.Generic;
using ManagerOfManagers.Core;
using UnityEngine;

namespace Illu.Managers.UI
{
    /// <summary>
    /// UI面板管理器。
    /// <para>
    /// 适配 MoM：
    /// 1. 基类提供 Instance（首次访问才创建）；
    /// 2. Canvas 的获取 + DontDestroyOnLoad 在 OnInit() 里——由 Hub 在所有
    ///    Awake 结束后的 Start 阶段统一驱动，必然执行一次，不依赖外部调用者；
    /// 3. panelDic / canvasTrans 私有，外部只通过方法访问。
    /// </para>
    /// </summary>
    public class UIManager : Singleton<UIManager>
    {
        /// <summary>UI 层，晚于数据层（JsonManager=10）。</summary>
        public override int Order => 20;

        //用于存储面板的容器
        private readonly Dictionary<string, BasePanel> panelDic = new Dictionary<string, BasePanel>();
        //Canvas 的引用，OnInit 之后才有值
        private Transform canvasTrans;

        public override void OnInit()
        {
            //Start 阶段：所有 Awake 已结束，场景里的 Canvas 一定找得到
            GameObject canvas = GameObject.Find("Canvas");
            if (canvas == null)
            {
                Debug.LogError("[UIManager] 场景里没有名为 Canvas 的物体，UI 无法初始化。");
                return;
            }

            canvasTrans = canvas.transform;
            //让Canvas不被删除，面板动态创建，删除不会有UI残留
            GameObject.DontDestroyOnLoad(canvasTrans.gameObject);
        }

        //显示面板
        public T ShowPanel<T>() where T : BasePanel
        {
            //需要保证泛型的类型和面板名一致
            string typeName = typeof(T).Name;
            //如果有这个面板，则直接返回出来，不去创建，保持唯一性
            if(panelDic.ContainsKey(typeName))
            {
                return panelDic[typeName] as T;
            }
            //根据类名创建panel
            GameObject panelObj = GameObject.Instantiate(Resources.Load<GameObject>("UI/" + typeName));
            //设置父对象
            panelObj.transform.SetParent(canvasTrans,false);
            //得到对象的脚本
            T panel = panelObj.GetComponent<T>();
            //将脚本加入字典
            panelDic.Add(typeName,panel);
            panel.ShowMe();
            return panel;
        }

        //隐藏面板
        /// <param name="isFade">希望淡出就传true，直接删除传false</param>
        public void HidePanel<T>(bool isFade = true) where T : BasePanel
        {
            //需要保证泛型的类型和面板名一致
            string typeName = typeof(T).Name;
            if(panelDic.ContainsKey(typeName))
            {
                if(isFade)
                {
                    //想要淡出
                    panelDic[typeName].HideMe(()=>{
                        //淡出结束后删除gameObjct和字典元素
                        GameObject.Destroy(panelDic[typeName].gameObject);
                        panelDic.Remove(typeName);
                    });
                }
                else
                {
                    //不淡出，直接删除
                    GameObject.Destroy(panelDic[typeName].gameObject);
                    panelDic.Remove(typeName);
                }
            }
        }

        //获得面板
        public T GetPanel<T>() where T : BasePanel
        {
            //需要保证泛型的类型和面板名一致
            string typeName = typeof(T).Name;
            if(panelDic.ContainsKey(typeName))
            {
                return panelDic[typeName] as T;
            }
            return null;
        }
    }
}
