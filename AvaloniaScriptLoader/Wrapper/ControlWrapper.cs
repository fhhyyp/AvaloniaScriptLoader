using Avalonia.Controls;
using Avalonia.Threading;
using ScriptLang.Runtime;
using AvaloniaScriptLoader.Builder;
using AvaloniaScriptLoader.Model;

namespace AvaloniaScriptLoader.Wrapper;

/// <summary>
/// 控件包装器 — 两阶段 setter 激活机制（IDisposable）
/// </summary>
public class ControlWrapper : IDisposable
{
    private readonly Control _control;
    private readonly ObjectValue _descriptor;
    private bool _disposed;

    /// <summary>待处理的属性变更队列（Activate 前累积）</summary>
    private readonly Queue<(string propertyName, Value value)> _pendingChanges = new();

    /// <summary>关联的控件描述符</summary>
    public ObjectValue Descriptor => _descriptor;

    /// <summary>关联的 Avalonia 控件</summary>
    public Control Control => _control;

    public ControlWrapper(Control control, ObjectValue descriptor)
    {
        _control = control ?? throw new ArgumentNullException(nameof(control));
        _descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
    }

    /// <summary>
    /// 激活：注入 Control 引用，替换所有 setter 为真实实现
    /// 必须在 UI 线程调用
    /// </summary>
    public void Activate()
    {
        // 1. 注入控件引用
        _descriptor.Properties[ControlMeta.ControlKey] = new ClrObjectValue(_control);
        _descriptor.Properties[ControlMeta.WrapperKey] = new ClrObjectValue(this);

        // 2. 替换所有延迟 setter 为真实 setter
        var props = new List<string>(_descriptor.Properties.Keys);
        foreach (var key in props)
        {
            if (PropertyNames.IsSetterMethod(key))
            {
                var propName = PropertyNames.SetterToPropertyName(key);
                _descriptor.Properties[key] = CreateRealSetter(key, propName);
            }
        }

        // 3. 应用 pending 变更（Build 前通过 DeferredSetter 累积的）
        while (_pendingChanges.TryDequeue(out var change))
        {
            SetProperty(change.propertyName, change.value);
        }
    }

    /// <summary>
    /// 设置属性值（外部调用入口）
    /// </summary>
    public void SetProperty(string propertyName, Value value)
    {
        // 更新描述符数据
        if (!string.IsNullOrEmpty(propertyName))
        {
            _descriptor.Properties[propertyName] = value;
        }

        // 在 UI 线程更新实际控件
        void UpdateControl()
        {
            var binder = new PropertyBinder();
            if (string.IsNullOrEmpty(propertyName))
                return;
            binder.SetControlProperty(_control, propertyName, value);
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            UpdateControl();
        }
        else
        {
            Dispatcher.UIThread.Post(UpdateControl);
        }
    }

    // ========================================================================
    // 内部方法
    // ========================================================================

    /// <summary>
    /// 创建真实 Setter 函数（替换延迟 Setter）
    /// </summary>
    private FunctionValue CreateRealSetter(string setterName, string propertyName)
    {
        var wrapper = this;
        return new FunctionValue(setterName, engineArgs =>
        {
            if (wrapper._disposed) return;
            var value = engineArgs.FirstOrDefault() ?? Value.Null;
            wrapper.SetProperty(propertyName, value);
        });
    }

    internal void EnqueuePendingChange(string propertyName, Value value)
    {
        _pendingChanges.Enqueue((propertyName, value));
    }

    // ========================================================================
    // IDisposable
    // ========================================================================

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pendingChanges.Clear();

        // 从描述符清除控件引用
        _descriptor.Properties.Remove(ControlMeta.ControlKey);
        _descriptor.Properties.Remove(ControlMeta.WrapperKey);

        Log.Debug($"[ControlWrapper] Disposed: {_control.GetType().Name}");
    }
}
