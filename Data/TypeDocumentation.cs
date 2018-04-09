using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using ICSharpCode.Decompiler.CSharp.Syntax;
using Mono.Cecil;
using ICSharpCode.Decompiler.TypeSystem;
using ICSharpCode.Decompiler.Semantics;
using ICSharpCode.Decompiler.CSharp.TypeSystem;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.IL;
using ICSharpCode.Decompiler.CSharp.Resolver;

namespace FuGetGallery
{
    public class TypeDocumentation
    {
        PackageTargetFramework framework;
        TypeDefinition typeDefinition;
        public TypeDefinition Definition => typeDefinition;

        readonly Lazy<ICSharpCode.Decompiler.CSharp.CSharpDecompiler> decompiler;
        readonly Lazy<ICSharpCode.Decompiler.CSharp.CSharpDecompiler> idecompiler;
        readonly ICSharpCode.Decompiler.CSharp.OutputVisitor.CSharpFormattingOptions format;

        public string SummaryText { get; }

        public TypeDocumentation (TypeDefinition typeDefinition, PackageTargetFramework framework, PackageAssemblyXmlDocs xmlDocs,
            Lazy<ICSharpCode.Decompiler.CSharp.CSharpDecompiler> decompiler, Lazy<ICSharpCode.Decompiler.CSharp.CSharpDecompiler> idecompiler,
            ICSharpCode.Decompiler.CSharp.OutputVisitor.CSharpFormattingOptions format)
        {
            this.typeDefinition = typeDefinition;
            this.framework = framework;
            this.decompiler = decompiler;
            this.idecompiler = idecompiler;
            this.format = format;

            format = ICSharpCode.Decompiler.CSharp.OutputVisitor.FormattingOptionsFactory.CreateMono ();
            format.SpaceBeforeMethodCallParentheses = false;
            format.SpaceBeforeMethodDeclarationParentheses = false;
            format.SpaceBeforeConstructorDeclarationParentheses = false;

            SummaryText = "";

            if (xmlDocs != null) {
                var tn = GetXmlName (typeDefinition);
                if (xmlDocs.MemberDocs.TryGetValue (tn, out var td)) {
                    SummaryText = td.SummaryText.Trim ();
                    if (SummaryText == "To be added.")
                        SummaryText = "";
                }
            }
        }

        string GetXmlName (TypeDefinition d)
        {
            return "T:" + d.FullName;
        }

        public Task<string> GetTypeCodeAsync (TypeDefinition type)
        {
            return Task.Run (() => {
                try {
                    var d = decompiler.Value;
                    if (d == null)
                        return "// No decompiler available";
                    var syntaxTree = d.DecompileType (new ICSharpCode.Decompiler.TypeSystem.FullTypeName (type.FullName));
                    var w = new HtmlWriter (new StringWriter(), framework);
                    w.Writer.Write("<div class=\"code\">");
                    syntaxTree.AcceptVisitor(new ICSharpCode.Decompiler.CSharp.OutputVisitor.CSharpOutputVisitor(w, format));
                    w.Writer.Write("</div>");
                    return w.Writer.ToString();
                }
                catch (Exception e) {
                    return "/* " + e.Message + " */";
                }
            });
        }

        public Task<string> GetTypeInterfaceCodeAsync (TypeDefinition type)
        {
            return Task.Run (() => {
                try {
                    var d = idecompiler.Value;
                    if (d == null)
                        return "// No decompiler available";
                    var syntaxTree = d.DecompileType (new ICSharpCode.Decompiler.TypeSystem.FullTypeName (type.FullName));
                    syntaxTree.AcceptVisitor(new RemoveNonInterfaceSyntaxVisitor { StartTypeName = type.Name });
                    var w = new HtmlWriter (new StringWriter(), framework);
                    w.Writer.Write("<div class=\"code\">");
                    syntaxTree.AcceptVisitor(new ICSharpCode.Decompiler.CSharp.OutputVisitor.CSharpOutputVisitor(w, format));
                    w.Writer.Write("</div>");
                    return w.Writer.ToString();
                }
                catch (Exception e) {
                    return "/* " + e + " */";
                }
            });
        }

        class HtmlWriter : ICSharpCode.Decompiler.CSharp.OutputVisitor.TokenWriter
        {
            readonly PackageTargetFramework framework;
            readonly TextWriter w;
            public TextWriter Writer => w;


            bool needsIndent = true;
            int indentLevel = 0;

            void WriteEncoded(string s)
            {
                w.Write (s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;"));
            }

            string GetClassAndLink(AstNode n, out string link)
            {
                link = null;
                if (n == null || n == AstNode.Null)
                    return "c-uk";
                while (n != null && n.Annotations.Count() == 0)
                    n = n.Parent;
                if (n == null || n == AstNode.Null)
                    return "c-uk";
                var t = n.Annotation<TypeResolveResult> ();
                if (t != null) {
                    if (n.NodeType == NodeType.TypeDeclaration) {
                        return "c-td";
                    }
                    var ns = t.Type.Namespace;
                    var name = t.Type.Name;
                    link = framework.FindTypeUrl (t.Type.FullName);
                    return "c-tr";
                }
                var u = n.Annotation<UsingScope> ();
                if (u != null)
                    return "c-nr";
                var m = n.Annotation<MemberResolveResult> ();
                if (m != null) {
                    if (m.Member.SymbolKind == SymbolKind.Method) {
                        if (n is MethodDeclaration)
                            return "c-md";
                        return "c-mr";
                    }
                    if (m.Member.SymbolKind == SymbolKind.Field) {
                        if (n is FieldDeclaration)
                            return "c-fd";
                        return "c-fr";
                    }
                    if (m.Member.SymbolKind == SymbolKind.Constructor) {
                        if (n is ConstructorDeclaration)
                            return "c-cd";
                        return "c-cr";
                    }
                    if (n is PropertyDeclaration)
                        return "c-pd";
                    return "c-pr";
                }
                var mg = n.Annotation<MethodGroupResolveResult> ();
                if (mg != null)
                    return "c-mr";
                var v = n.Annotation<ILVariableResolveResult> ();
                if (v != null) {
                    if (v.Variable.Kind == VariableKind.Parameter)
                        return "c-ar";
                    return "c-uk";
                }
                var c = n.Annotation<ConstantResolveResult> ();
                if (c != null) {
                    return "c-fr";
                }

                // Console.WriteLine(n.Annotations.FirstOrDefault());
                return "c-uk";
            }
            void WriteIndent()
            {
                if (!needsIndent) return;
                needsIndent = false;
                w.Write(new String(' ', indentLevel * 4));
            }
            public HtmlWriter(TextWriter w, PackageTargetFramework framework)
            {
                this.w = w;
                this.framework = framework;
            }
            public override void StartNode(AstNode node)
            {
            }

            public override void EndNode(AstNode node)
            {
            }

            public override void Indent()
            {
                indentLevel++;                
            }

            public override void Space()
            {
                WriteIndent ();
                w.Write(" ");
            }

            public override void NewLine()
            {
                w.WriteLine();
                needsIndent = true;
            }

            public override void Unindent()
            {
                indentLevel--;
            }

            public override void WriteComment(CommentType commentType, string content)
            {
            }

            public override void WriteIdentifier(Identifier identifier)
            {
                WriteIndent ();
                var c = GetClassAndLink(identifier, out var link);
                if (link != null)  {
                    w.Write("<a class=\"");
                    w.Write(c);
                    w.Write("\" href=\"");
                    WriteEncoded(link);
                    w.Write("\">");
                    WriteEncoded(identifier.Name);
                    w.Write("</a>");
                }
                else {
                    w.Write("<span class=\"");
                    w.Write(c);
                    w.Write("\">");
                    WriteEncoded(identifier.Name);
                    w.Write("</span>");
                }
            }

            public override void WriteKeyword(Role role, string keyword)
            {
                WriteIndent ();
                w.Write("<span class=\"c-kw\">");
                WriteEncoded(keyword);
                w.Write("</span>");
            }

            public override void WritePreProcessorDirective(PreProcessorDirectiveType type, string argument)
            {
            }

            public override void WritePrimitiveType(string type)
            {
                WriteIndent ();
                w.Write("<span class=\"c-tr\">");
                w.Write(type);
                w.Write("</span>");
            }

            public override void WritePrimitiveValue(object value, string literalValue = null)
            {
                WriteIndent ();
                if (value == null) {
                    w.Write("<span class=\"c-nl\">null</span>");
                    return;
                }
                if (value is bool b) {
                    w.Write(b ? "<span class=\"c-nl\">true</span>" : "<span class=\"c-nl\">false</span>");
                    return;
                }
                if (value is string s) {
                    w.Write("<span class=\"c-st\">\"");
                    WriteEncoded(s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\t", "\\t").Replace("\r", "\\r").Replace("\n", "\\n"));
                    w.Write("\"</span>");
                    return;
                }
                w.Write("<span class=\"c-nu\">");
                w.Write(Convert.ToString (value, System.Globalization.CultureInfo.InvariantCulture));
                w.Write("</span>");
            }

            public override void WriteToken(Role role, string token)
            {
                WriteIndent ();
                w.Write(token);
            }
        }

        class RemoveNonInterfaceSyntaxVisitor : DepthFirstAstVisitor
        {
            public string StartTypeName;
            readonly HashSet<string> skipAttrs = new HashSet<string> {
                "CLSCompliant",
                "CompilerGenerated",
                "EditorBrowsable",
                "DebuggerDisplay",
            };
            void RemoveAttributes (IEnumerable<ICSharpCode.Decompiler.CSharp.Syntax.AttributeSection> attrs)
            {
                if (attrs == null)
                    return;
                foreach (var s in attrs.ToList ()) {
                    var toRemove = s.Attributes.Where (x => x.Type is SimpleType t && skipAttrs.Contains(t.Identifier)).ToList();
                    foreach (var a in toRemove) {
                        a.Remove();
                    }
                    if (s.Children.Count() == 0)
                        s.Remove();
                }
            }
            public override void VisitMethodDeclaration(MethodDeclaration d)
            {
                if (d.Modifiers.HasFlag(Modifiers.Private) || d.Modifiers.HasFlag(Modifiers.Internal)
                    || d.Modifiers.HasFlag(Modifiers.Override)
                    || d.PrivateImplementationType != AstType.Null
                    ) {
                    d.Remove();
                }
                else {
                    RemoveAttributes (d.Attributes);
                    base.VisitMethodDeclaration(d);
                }
            }
            public override void VisitConstructorDeclaration(ConstructorDeclaration d)
            {
                if (d.Modifiers.HasFlag(Modifiers.Private) || d.Modifiers.HasFlag(Modifiers.Static) || d.Modifiers.HasFlag(Modifiers.Internal)) {
                    d.Remove();
                }
                else {
                    RemoveAttributes (d.Attributes);
                    base.VisitConstructorDeclaration(d);
                }
            }
            public override void VisitFieldDeclaration(FieldDeclaration d)
            {
                if (d.Modifiers.HasFlag(Modifiers.Private) || d.Modifiers.HasFlag(Modifiers.Internal)) {
                    d.Remove();
                }
                else {
                    RemoveAttributes (d.Attributes);
                    base.VisitFieldDeclaration(d);
                }
            }
            public override void VisitPropertyDeclaration(PropertyDeclaration d)
            {
                if (d.Modifiers.HasFlag(Modifiers.Private) || d.Modifiers.HasFlag(Modifiers.Internal)
                    || d.PrivateImplementationType != AstType.Null) {
                    d.Remove();
                }
                else {
                    RemoveAttributes (d.Attributes);
                    if (d.Getter != null) {
                        if (d.Getter.Modifiers.HasFlag(Modifiers.Private) || d.Getter.Modifiers.HasFlag(Modifiers.Internal)) {
                            d.Getter.Remove();
                        }
                        else {
                            RemoveAttributes (d.Getter.Attributes);
                        }
                    }
                    if (d.Setter != null) {
                        if (d.Setter.Modifiers.HasFlag(Modifiers.Private) || d.Setter.Modifiers.HasFlag(Modifiers.Internal)) {
                            d.Setter.Remove();
                        }
                        else {
                            RemoveAttributes (d.Setter.Attributes);
                        }
                    }
                    base.VisitPropertyDeclaration(d);
                }
            }
            public override void VisitEventDeclaration(EventDeclaration d)
            {
                if (d.Modifiers.HasFlag(Modifiers.Private) || d.Modifiers.HasFlag(Modifiers.Internal)) {
                    d.Remove();
                }
                else {
                    RemoveAttributes (d.Attributes);
                    base.VisitEventDeclaration(d);
                }
            }
            public override void VisitCustomEventDeclaration(CustomEventDeclaration d)
            {
                if (d.Modifiers.HasFlag(Modifiers.Private) || d.Modifiers.HasFlag(Modifiers.Internal)) {
                    d.Remove();
                }
                else {
                    RemoveAttributes (d.Attributes);
                    RemoveAttributes (d.AddAccessor?.Attributes);
                    RemoveAttributes (d.RemoveAccessor?.Attributes);
                    base.VisitCustomEventDeclaration(d);
                }
            }
            public override void VisitTypeDeclaration(TypeDeclaration d)
            {
                if (d.Name != StartTypeName &&
                    (d.Modifiers.HasFlag(Modifiers.Private))) {
                    d.Remove();
                }
                else {
                    RemoveAttributes (d.Attributes);
                    base.VisitTypeDeclaration(d);
                }
            }
        }
    }
}
