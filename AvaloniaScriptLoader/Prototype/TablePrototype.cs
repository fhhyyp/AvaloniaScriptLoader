using ScriptLang;
using ScriptLang.Runtime;
using AvaloniaScriptLoader.Model;

namespace AvaloniaScriptLoader.Prototype
{
    /// <summary>
    /// TableValue 脚本侧原型扩展。
    ///
    /// 使脚本可通过 .get() / .value / .set() / .addRow() / .removeRow() 操作 TableValue，
    /// 替代原来手动构建 FunctionValue 字典的 ObjectValue 包装方式。
    ///
    /// 匹配目标：ClrObjectValue { Value is TableValue }
    /// </summary>
    [PrototypeExtension(PushThis = true, NamingFormat = NamingFormat.Js)]
    public partial class TablePrototype
    {
        public partial bool IsTarget(Value value)
        {
            return value is ClrObjectValue clr && clr.Value is TableValue;
        }

        /// <summary>table.get() → 返回 ArrayValue（触发依赖追踪）</summary>
        [PrototypeFunction]
        private static ArrayValue Get(TableValue tv)
        {
            return tv.Get();
        }

        /// <summary>table.value → 读取当前值</summary>
        [PrototypeProperty]
        private static ArrayValue Value(TableValue tv)
        {
            return tv.Get();
        }

        /// <summary>table.set(array) → 替换全部数据</summary>
        [PrototypeFunction]
        private static void Set(TableValue tv, ArrayValue av)
        {
            tv.Set(av);
        }

        /// <summary>table.addRow(row) → 追加一行</summary>
        [PrototypeFunction]
        private static void AddRow(TableValue tv, ObjectValue row)
        {
            tv.AddRow(row);
        }

        /// <summary>table.removeRow(index) → 移除指定索引行</summary>
        [PrototypeFunction]
        private static void RemoveRow(TableValue tv, Value idxVal)
        {
            if (idxVal.IsNumber)
                tv.RemoveRow(idxVal.As<int>());
        }
    }
}
