using System;
using System.Windows.Automation;

namespace Explorer2Dialog
{
    internal static class Uia
    {
        public static Condition Eq(AutomationProperty property, object value)
        {
            return new PropertyCondition(property, value);
        }

        public static Condition Id(string value) { return Eq(AutomationElement.AutomationIdProperty, value); }
        public static Condition Class(string value) { return Eq(AutomationElement.ClassNameProperty, value); }
        public static Condition Type(ControlType value) { return Eq(AutomationElement.ControlTypeProperty, value); }

        public static AutomationElement Find(AutomationElement root, params Condition[] all)
        {
            return root == null ? null : root.FindFirst(TreeScope.Descendants,
                all.Length == 1 ? all[0] : new AndCondition(all));
        }

        public static AutomationElement Child(AutomationElement root, params Condition[] all)
        {
            return root == null ? null : root.FindFirst(TreeScope.Children,
                all.Length == 1 ? all[0] : new AndCondition(all));
        }

        public static T Pattern<T>(AutomationElement node, AutomationPattern pattern) where T : class
        {
            object result;
            return node != null && node.TryGetCurrentPattern(pattern, out result) ? result as T : null;
        }

        public static string Value(AutomationElement node)
        {
            var value = Pattern<ValuePattern>(node, ValuePattern.Pattern);
            return value == null ? null : value.Current.Value;
        }

        public static AutomationElement Breadcrumb(AutomationElement rebar)
        {
            return Find(rebar, Class("ToolbarWindow32"), Id("1001"));
        }

        public static string CommittedFolder(AutomationElement breadcrumb)
        {
            if (breadcrumb == null || breadcrumb.Current.IsOffscreen) return null;
            var name = breadcrumb.Current.Name;
            if (name == null) return null;
            var separator = name.IndexOf(": ", StringComparison.Ordinal);
            if (separator < 0) separator = name.IndexOf("：", StringComparison.Ordinal);
            return FolderPath.Normalize(separator < 0 ? name : name.Substring(separator + 1).TrimStart());
        }
    }
}
