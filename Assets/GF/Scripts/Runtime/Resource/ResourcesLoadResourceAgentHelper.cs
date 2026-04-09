//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2021 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

using GameFramework;
using GameFramework.FileSystem;
using GameFramework.Resource;
using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityGameFramework.Runtime
{
    /// <summary>
    /// Resources 后端加载资源代理辅助器。
    /// </summary>
    public sealed class ResourcesLoadResourceAgentHelper : LoadResourceAgentHelperBase
    {
        /// <summary>
        /// Resources 后端占位资源对象。
        /// </summary>
        private sealed class ResourcesDummyResource
        {
        }

        /// <summary>
        /// 当前加载的资源名称。
        /// </summary>
        private string m_AssetName = null;

        /// <summary>
        /// 当前加载的资源类型。
        /// </summary>
        private Type m_AssetType = null;

        /// <summary>
        /// 上次上报的加载进度。
        /// </summary>
        private float m_LastProgress = 0f;

        /// <summary>
        /// Resources 异步加载请求。
        /// </summary>
        private ResourceRequest m_ResourceRequest = null;

        /// <summary>
        /// 场景异步加载操作。
        /// </summary>
        private AsyncOperation m_SceneAsyncOperation = null;

        /// <summary>
        /// 加载资源代理辅助器异步加载资源更新事件。
        /// </summary>
        private EventHandler<LoadResourceAgentHelperUpdateEventArgs> m_LoadResourceAgentHelperUpdateEventHandler = null;

        /// <summary>
        /// 加载资源代理辅助器异步读取资源文件完成事件。
        /// </summary>
        private EventHandler<LoadResourceAgentHelperReadFileCompleteEventArgs> m_LoadResourceAgentHelperReadFileCompleteEventHandler = null;

        /// <summary>
        /// 加载资源代理辅助器异步读取资源二进制流完成事件。
        /// </summary>
        private EventHandler<LoadResourceAgentHelperReadBytesCompleteEventArgs> m_LoadResourceAgentHelperReadBytesCompleteEventHandler = null;

        /// <summary>
        /// 加载资源代理辅助器异步将资源二进制流转换为加载对象完成事件。
        /// </summary>
        private EventHandler<LoadResourceAgentHelperParseBytesCompleteEventArgs> m_LoadResourceAgentHelperParseBytesCompleteEventHandler = null;

        /// <summary>
        /// 加载资源代理辅助器异步加载资源完成事件。
        /// </summary>
        private EventHandler<LoadResourceAgentHelperLoadCompleteEventArgs> m_LoadResourceAgentHelperLoadCompleteEventHandler = null;

        /// <summary>
        /// 加载资源代理辅助器错误事件。
        /// </summary>
        private EventHandler<LoadResourceAgentHelperErrorEventArgs> m_LoadResourceAgentHelperErrorEventHandler = null;

        /// <summary>
        /// 加载资源代理辅助器异步加载资源更新事件。
        /// </summary>
        public override event EventHandler<LoadResourceAgentHelperUpdateEventArgs> LoadResourceAgentHelperUpdate
        {
            add
            {
                m_LoadResourceAgentHelperUpdateEventHandler += value;
            }
            remove
            {
                m_LoadResourceAgentHelperUpdateEventHandler -= value;
            }
        }

        /// <summary>
        /// 加载资源代理辅助器异步读取资源文件完成事件。
        /// </summary>
        public override event EventHandler<LoadResourceAgentHelperReadFileCompleteEventArgs> LoadResourceAgentHelperReadFileComplete
        {
            add
            {
                m_LoadResourceAgentHelperReadFileCompleteEventHandler += value;
            }
            remove
            {
                m_LoadResourceAgentHelperReadFileCompleteEventHandler -= value;
            }
        }

        /// <summary>
        /// 加载资源代理辅助器异步读取资源二进制流完成事件。
        /// </summary>
        public override event EventHandler<LoadResourceAgentHelperReadBytesCompleteEventArgs> LoadResourceAgentHelperReadBytesComplete
        {
            add
            {
                m_LoadResourceAgentHelperReadBytesCompleteEventHandler += value;
            }
            remove
            {
                m_LoadResourceAgentHelperReadBytesCompleteEventHandler -= value;
            }
        }

        /// <summary>
        /// 加载资源代理辅助器异步将资源二进制流转换为加载对象完成事件。
        /// </summary>
        public override event EventHandler<LoadResourceAgentHelperParseBytesCompleteEventArgs> LoadResourceAgentHelperParseBytesComplete
        {
            add
            {
                m_LoadResourceAgentHelperParseBytesCompleteEventHandler += value;
            }
            remove
            {
                m_LoadResourceAgentHelperParseBytesCompleteEventHandler -= value;
            }
        }

        /// <summary>
        /// 加载资源代理辅助器异步加载资源完成事件。
        /// </summary>
        public override event EventHandler<LoadResourceAgentHelperLoadCompleteEventArgs> LoadResourceAgentHelperLoadComplete
        {
            add
            {
                m_LoadResourceAgentHelperLoadCompleteEventHandler += value;
            }
            remove
            {
                m_LoadResourceAgentHelperLoadCompleteEventHandler -= value;
            }
        }

        /// <summary>
        /// 加载资源代理辅助器错误事件。
        /// </summary>
        public override event EventHandler<LoadResourceAgentHelperErrorEventArgs> LoadResourceAgentHelperError
        {
            add
            {
                m_LoadResourceAgentHelperErrorEventHandler += value;
            }
            remove
            {
                m_LoadResourceAgentHelperErrorEventHandler -= value;
            }
        }

        /// <summary>
        /// 通过加载资源代理辅助器开始异步读取资源文件。
        /// </summary>
        /// <param name="fullPath">要加载资源的完整路径名。</param>
        public override void ReadFile(string fullPath)
        {
            if (!IsHandlerValidForRead())
            {
                return;
            }

            LoadResourceAgentHelperReadFileCompleteEventArgs loadResourceAgentHelperReadFileCompleteEventArgs = LoadResourceAgentHelperReadFileCompleteEventArgs.Create(new ResourcesDummyResource());
            m_LoadResourceAgentHelperReadFileCompleteEventHandler(this, loadResourceAgentHelperReadFileCompleteEventArgs);
            ReferencePool.Release(loadResourceAgentHelperReadFileCompleteEventArgs);
        }

        /// <summary>
        /// 通过加载资源代理辅助器开始异步读取资源文件。
        /// </summary>
        /// <param name="fileSystem">要加载资源的文件系统。</param>
        /// <param name="name">要加载资源的名称。</param>
        public override void ReadFile(IFileSystem fileSystem, string name)
        {
            ReadFile(name);
        }

        /// <summary>
        /// 通过加载资源代理辅助器开始异步读取资源二进制流。
        /// </summary>
        /// <param name="fullPath">要加载资源的完整路径名。</param>
        public override void ReadBytes(string fullPath)
        {
            FireUnsupportedError("Resources 后端不支持读取二进制资源。");
        }

        /// <summary>
        /// 通过加载资源代理辅助器开始异步读取资源二进制流。
        /// </summary>
        /// <param name="fileSystem">要加载资源的文件系统。</param>
        /// <param name="name">要加载资源的名称。</param>
        public override void ReadBytes(IFileSystem fileSystem, string name)
        {
            FireUnsupportedError("Resources 后端不支持读取二进制资源。");
        }

        /// <summary>
        /// 通过加载资源代理辅助器开始异步将资源二进制流转换为加载对象。
        /// </summary>
        /// <param name="bytes">要加载资源的二进制流。</param>
        public override void ParseBytes(byte[] bytes)
        {
            FireUnsupportedError("Resources 后端不支持解析二进制资源。");
        }

        /// <summary>
        /// 通过加载资源代理辅助器开始异步加载资源。
        /// </summary>
        /// <param name="resource">资源。</param>
        /// <param name="assetName">要加载的资源名称。</param>
        /// <param name="assetType">要加载资源的类型。</param>
        /// <param name="isScene">要加载的资源是否是场景。</param>
        public override void LoadAsset(object resource, string assetName, Type assetType, bool isScene)
        {
            if (!IsHandlerValidForLoad())
            {
                return;
            }

            if (string.IsNullOrEmpty(assetName))
            {
                FireError(LoadResourceStatus.AssetError, "Resources 后端加载资源失败，资源名称无效。");
                return;
            }

            m_AssetName = assetName;
            m_AssetType = assetType;
            m_LastProgress = 0f;

            if (isScene)
            {
                string sceneName = SceneComponent.GetSceneName(assetName);
                if (string.IsNullOrEmpty(sceneName))
                {
                    FireError(LoadResourceStatus.AssetError, Utility.Text.Format("Resources 后端加载场景失败，场景名无效：'{0}'.", assetName));
                    return;
                }

                m_SceneAsyncOperation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
                if (m_SceneAsyncOperation == null)
                {
                    FireError(LoadResourceStatus.NotExist, Utility.Text.Format("Resources 后端加载场景失败：'{0}'.", assetName));
                }

                return;
            }

            m_ResourceRequest = assetType != null ? Resources.LoadAsync(assetName, assetType) : Resources.LoadAsync(assetName);
            if (m_ResourceRequest == null)
            {
                FireError(LoadResourceStatus.NotExist, Utility.Text.Format("Resources 后端加载资源失败：'{0}'.", assetName));
            }
        }

        /// <summary>
        /// 重置加载资源代理辅助器。
        /// </summary>
        public override void Reset()
        {
            m_AssetName = null;
            m_AssetType = null;
            m_LastProgress = 0f;
            m_ResourceRequest = null;
            m_SceneAsyncOperation = null;
        }

        /// <summary>
        /// Unity 每帧更新回调，用于推进异步加载状态。
        /// </summary>
        private void Update()
        {
            UpdateResourceRequest();
            UpdateSceneAsyncOperation();
        }

        /// <summary>
        /// 检查读取相关事件处理器是否有效。
        /// </summary>
        /// <returns>事件处理器是否有效。</returns>
        private bool IsHandlerValidForRead()
        {
            if (m_LoadResourceAgentHelperReadFileCompleteEventHandler == null || m_LoadResourceAgentHelperUpdateEventHandler == null || m_LoadResourceAgentHelperErrorEventHandler == null)
            {
                Log.Fatal("Load resource agent helper handler is invalid.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// 检查加载相关事件处理器是否有效。
        /// </summary>
        /// <returns>事件处理器是否有效。</returns>
        private bool IsHandlerValidForLoad()
        {
            if (m_LoadResourceAgentHelperLoadCompleteEventHandler == null || m_LoadResourceAgentHelperUpdateEventHandler == null || m_LoadResourceAgentHelperErrorEventHandler == null)
            {
                Log.Fatal("Load resource agent helper handler is invalid.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// 处理 Resources 资源加载进度与完成逻辑。
        /// </summary>
        private void UpdateResourceRequest()
        {
            if (m_ResourceRequest == null)
            {
                return;
            }

            if (m_ResourceRequest.isDone)
            {
                if (m_ResourceRequest.asset != null)
                {
                    LoadResourceAgentHelperLoadCompleteEventArgs loadResourceAgentHelperLoadCompleteEventArgs = LoadResourceAgentHelperLoadCompleteEventArgs.Create(m_ResourceRequest.asset);
                    m_LoadResourceAgentHelperLoadCompleteEventHandler(this, loadResourceAgentHelperLoadCompleteEventArgs);
                    ReferencePool.Release(loadResourceAgentHelperLoadCompleteEventArgs);
                }
                else
                {
                    FireError(LoadResourceStatus.AssetError, Utility.Text.Format("Resources 后端加载资源失败：'{0}'.", m_AssetName));
                }

                m_ResourceRequest = null;
                m_AssetName = null;
                m_AssetType = null;
                m_LastProgress = 0f;
                return;
            }

            if (m_ResourceRequest.progress != m_LastProgress)
            {
                m_LastProgress = m_ResourceRequest.progress;
                LoadResourceAgentHelperUpdateEventArgs loadResourceAgentHelperUpdateEventArgs = LoadResourceAgentHelperUpdateEventArgs.Create(LoadResourceProgress.LoadAsset, m_ResourceRequest.progress);
                m_LoadResourceAgentHelperUpdateEventHandler(this, loadResourceAgentHelperUpdateEventArgs);
                ReferencePool.Release(loadResourceAgentHelperUpdateEventArgs);
            }
        }

        /// <summary>
        /// 处理场景加载进度与完成逻辑。
        /// </summary>
        private void UpdateSceneAsyncOperation()
        {
            if (m_SceneAsyncOperation == null)
            {
                return;
            }

            if (m_SceneAsyncOperation.isDone)
            {
                if (m_SceneAsyncOperation.allowSceneActivation)
                {
                    SceneAsset sceneAsset = new SceneAsset();
                    LoadResourceAgentHelperLoadCompleteEventArgs loadResourceAgentHelperLoadCompleteEventArgs = LoadResourceAgentHelperLoadCompleteEventArgs.Create(sceneAsset);
                    m_LoadResourceAgentHelperLoadCompleteEventHandler(this, loadResourceAgentHelperLoadCompleteEventArgs);
                    ReferencePool.Release(loadResourceAgentHelperLoadCompleteEventArgs);
                }
                else
                {
                    FireError(LoadResourceStatus.AssetError, Utility.Text.Format("Resources 后端加载场景失败：'{0}'.", m_AssetName));
                }

                m_SceneAsyncOperation = null;
                m_AssetName = null;
                m_AssetType = null;
                m_LastProgress = 0f;
                return;
            }

            if (m_SceneAsyncOperation.progress != m_LastProgress)
            {
                m_LastProgress = m_SceneAsyncOperation.progress;
                LoadResourceAgentHelperUpdateEventArgs loadResourceAgentHelperUpdateEventArgs = LoadResourceAgentHelperUpdateEventArgs.Create(LoadResourceProgress.LoadScene, m_SceneAsyncOperation.progress);
                m_LoadResourceAgentHelperUpdateEventHandler(this, loadResourceAgentHelperUpdateEventArgs);
                ReferencePool.Release(loadResourceAgentHelperUpdateEventArgs);
            }
        }

        /// <summary>
        /// 触发不支持功能的错误回调。
        /// </summary>
        /// <param name="errorMessage">错误信息。</param>
        private void FireUnsupportedError(string errorMessage)
        {
            FireError(LoadResourceStatus.AssetError, errorMessage);
        }

        /// <summary>
        /// 触发资源加载错误回调。
        /// </summary>
        /// <param name="status">错误状态。</param>
        /// <param name="errorMessage">错误信息。</param>
        private void FireError(LoadResourceStatus status, string errorMessage)
        {
            if (m_LoadResourceAgentHelperErrorEventHandler == null)
            {
                Log.Fatal("Load resource agent helper handler is invalid.");
                return;
            }

            LoadResourceAgentHelperErrorEventArgs loadResourceAgentHelperErrorEventArgs = LoadResourceAgentHelperErrorEventArgs.Create(status, errorMessage);
            m_LoadResourceAgentHelperErrorEventHandler(this, loadResourceAgentHelperErrorEventArgs);
            ReferencePool.Release(loadResourceAgentHelperErrorEventArgs);
        }
    }
}
