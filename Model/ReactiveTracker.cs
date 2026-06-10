namespace AvaloniaScriptLoader.Model;

/// <summary>
/// 响应式依赖追踪器（线程静态）
///
/// 模仿 Vue 3 的 activeEffect 机制：
/// - ComputedValue 求值时将自己注册为 Current
/// - InpcValue.Get() 被调用时检查 Current 并建立依赖关系
/// - 支持嵌套 ComputedValue（栈结构）
/// </summary>
internal static class ReactiveTracker
{
    [ThreadStatic]
    private static Stack<ComputedValue>? _stack;

    /// <summary>当前正在求值的 ComputedValue（栈顶）</summary>
    public static ComputedValue? Current
    {
        get
        {
            if (_stack == null || _stack.Count == 0) return null;
            return _stack.Peek();
        }
    }

    /// <summary>推入新的 ComputedValue 到求值栈</summary>
    public static void Push(ComputedValue cv)
    {
        _stack ??= new Stack<ComputedValue>();
        _stack.Push(cv);
    }

    /// <summary>从求值栈弹出 ComputedValue</summary>
    public static void Pop()
    {
        _stack?.Pop();
    }
}
