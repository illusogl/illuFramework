using UnityEngine;
using UnityEngine.Events;

namespace Illu.Managers.UI
{
    /// <summary>
    /// 面板基类：CanvasGroup 淡入淡出。面板预制体约定放在 Resources/UI/面板类名。
    /// </summary>
    public abstract class BasePanel : MonoBehaviour
    {
        protected CanvasGroup canvasGroup;
        public float alphaSpeed = 5;
        protected bool isShow = true;
        //隐藏时候的删除操作委托给外面做
        protected UnityAction HideCallBack;

        protected virtual void Awake()
        {
            canvasGroup = this.GetComponent<CanvasGroup>();
            if(canvasGroup == null)
                canvasGroup = this.gameObject.AddComponent<CanvasGroup>();
        }

        protected virtual void Start()
        {
            Init();
        }

        protected virtual void Update()
        {
            if(isShow && canvasGroup.alpha != 1)
            {
                canvasGroup.alpha += Time.deltaTime * alphaSpeed;
                if(canvasGroup.alpha >= 1)
                    canvasGroup.alpha = 1;
            }
            if(!isShow)
            {
                canvasGroup.alpha -= Time.deltaTime * alphaSpeed;
                if(canvasGroup.alpha <= 0)
                {
                    canvasGroup.alpha = 0;
                    //调用删除组件
                    HideCallBack?.Invoke();
                }
            }
        }

        public abstract void Init();

        public virtual void ShowMe()
        {
            isShow = true;
            //先隐藏，慢慢显示
            canvasGroup.alpha = 0;
        }

        public virtual void HideMe(UnityAction ua)
        {
            isShow = false;
            //先显示，慢慢隐藏
            canvasGroup.alpha = 1;
            HideCallBack = ua;
        }
    }
}
