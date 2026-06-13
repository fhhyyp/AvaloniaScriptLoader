using ScriptLang;
using ScriptLang.Runtime;
using AvaloniaScriptLoader.Model;

namespace AvaloniaScriptLoader.Prototype
{
    /// <summary>
    /// InpcValue 脚本侧原型扩展。
    ///
    /// 使脚本可通过 .get() / .set() / .value / .push() / .pop() / .removeAt() 操作 InpcValue，
    /// 替代原来手动构建 FunctionValue 字典的 ObjectValue 包装方式。
    ///
    /// 匹配目标：ClrObjectValue { Value is InpcValue }
    /// </summary>
    [PrototypeExtension(PushThis = true, NamingFormat = NamingFormat.Js)]
    public partial class InpcPrototype
    {
        public partial bool IsTarget(Value value)
        {
            return value is ClrObjectValue clr && clr.Value is InpcValue;
        }

        /// <summary>inpc.get() → 返回当前值（触发依赖追踪）</summary>
        [PrototypeFunction]
        private static Value Get(InpcValue inpc)
        {
            return inpc.Get();
        }

        /// <summary>inpc.set(newValue) → 设置新值并通知订阅者</summary>
        [PrototypeFunction]
        private static void Set(InpcValue inpc, Value newValue)
        {
            inpc.Set(newValue);
        }

        /// <summary>inpc.value → 读取当前值</summary>
        [PrototypeProperty]
        private static Value Value(InpcValue inpc)
        {
            return inpc.Get();
        }

        /// <summary>inpc.push(item) → 添加元素并通知</summary>
        [PrototypeFunction]
        private static void Push(InpcValue inpc, Value item)
        {
            var val = inpc.Get();
            if (val is ArrayValue av)
            {
                av.Add(item);
                inpc.Set(av);
            }
        }

        /// <summary>inpc.pop() → 移除并返回最后一个元素</summary>
        [PrototypeFunction]
        private static Value Pop(InpcValue inpc)
        {
            var val = inpc.Get();
            if (val is ArrayValue av && av.Elements.Count > 0)
            {
                var popped = av.Pop();
                inpc.Set(av);
                return popped;
            }
            return ScriptLang.Runtime.Value.Null;
        }

        /// <summary>inpc.removeAt(index) → 移除指定索引元素并通知</summary>
        [PrototypeFunction]
        private static void RemoveAt(InpcValue inpc, Value idxVal)
        {
            var val = inpc.Get();
            if (val is ArrayValue av && idxVal.IsNumber_Int)
            {
                int idx = idxVal.As<int>();
                if (idx >= 0 && idx < av.Elements.Count)
                {
                    av.RemoveAt(idx);
                    inpc.Set(av);
                }
            }
        }
    }
}
