// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Extensions;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp.Utilities;
using Microsoft.CodeAnalysis.Editor.Host;
using Microsoft.CodeAnalysis.Editor.Implementation.AutomaticCompletion;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.Utilities;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Editor.CSharp.AutomaticCompletion
{
    /// <summary>
    /// csharp automatic line ender command handler
    /// </summary>
    [ExportCommandHandler(PredefinedCommandHandlerNames.AutomaticLineEnder, ContentTypeNames.CSharpContentType)]
    [Order(After = PredefinedCommandHandlerNames.Completion)]
    internal class AutomaticLineEnderCommandHandler : AbstractAutomaticLineEnderCommandHandler
    {
        [ImportingConstructor]
        public AutomaticLineEnderCommandHandler(
            IWaitIndicator waitIndicator,
            ITextUndoHistoryRegistry undoRegistry,
            IEditorOperationsFactoryService editorOperations)
            : base(waitIndicator, undoRegistry, editorOperations)
        {
        }

        protected override void NextAction(IEditorOperations editorOperation, Action nextAction)
        {
            editorOperation.InsertNewLine();
        }

        protected override void FormatAndApply(Document document, int position, CancellationToken cancellationToken)
        {
            var root = document.GetSyntaxRootAsync(cancellationToken).WaitAndGetResult(cancellationToken);

            var endToken = root.FindToken(position);
            if (endToken.IsMissing)
            {
                return;
            }

            var ranges = FormattingRangeHelper.FindAppropriateRange(endToken, useDefaultRange: false);
            if (ranges == null)
            {
                return;
            }

            var startToken = ranges.Value.Item1;
            if (startToken.IsMissing || startToken.Kind() == SyntaxKind.None)
            {
                return;
            }

            var changes = Formatter.GetFormattedTextChanges(root, new TextSpan[] { TextSpan.FromBounds(startToken.SpanStart, endToken.Span.End) }, document.Project.Solution.Workspace, options: null, // use default
                rules: null, // use default
                cancellationToken: cancellationToken);

            document.ApplyTextChanges(changes.ToArray(), cancellationToken);
        }

        protected override string GetEndingString(Document document, int position, CancellationToken cancellationToken)
        {
            // prepare expansive information from document
            var tree = document.GetSyntaxTreeAsync(cancellationToken).WaitAndGetResult(cancellationToken);
            var root = tree.GetRootAsync(cancellationToken).WaitAndGetResult(cancellationToken);
            var text = tree.GetTextAsync(cancellationToken).WaitAndGetResult(cancellationToken);

            var owningNode = GetOwningNode(root, position);
            if (owningNode == null)
            {
                return null;
            }

            SyntaxToken lastToken;
            if (!TryGetLastToken(text, position, owningNode, out lastToken))
            {
                return null;
            }

            if (!CheckLocation(text, position, owningNode, lastToken))
            {
                return null;
            }

            // so far so good. we only add semi-colon if it makes statement syntax error free
            var semicolon = SyntaxFacts.GetText(SyntaxKind.SemicolonToken);

            var textToParse = owningNode.NormalizeWhitespace().ToFullString() + semicolon;

            // currently, Parsing a field is not supported. as a workaround, wrap the field in a type and parse
            var node = owningNode.TypeSwitch(
                (UsingDirectiveSyntax n) => (SyntaxNode)SyntaxFactory.ParseCompilationUnit(textToParse, options: (CSharpParseOptions)tree.Options),
                (BaseFieldDeclarationSyntax n) => SyntaxFactory.ParseCompilationUnit(WrapInType(textToParse), options: (CSharpParseOptions)tree.Options),
                (StatementSyntax n) => SyntaxFactory.ParseStatement(textToParse, options: (CSharpParseOptions)tree.Options));

            if (node == null)
            {
                return null;
            }

            return node.ContainsDiagnostics ? null : semicolon;
        }

        /// <summary>
        /// wrap field in type
        /// </summary>
        private string WrapInType(string textToParse)
        {
            return "class C { " + textToParse + " }";
        }

        /// <summary>
        /// make sure current location is okay to put semicolon
        /// </summary>
        private static bool CheckLocation(SourceText text, int position, SyntaxNode owningNode, SyntaxToken lastToken)
        {
            var line = text.Lines.GetLineFromPosition(position);

            // if caret is at the end of the line and containing statement is expression statement
            // don't do anything
            if (position == line.End && owningNode is ExpressionStatementSyntax)
            {
                return false;
            }

            var locatedAtTheEndOfLine = LocatedAtTheEndOfLine(line, lastToken);

            // make sure that there is no trailing text after last token on the line if it is not at the end of the line
            if (!locatedAtTheEndOfLine)
            {
                var endingString = text.ToString(TextSpan.FromBounds(lastToken.Span.End, line.End));
                if (!string.IsNullOrWhiteSpace(endingString))
                {
                    return false;
                }
            }

            // check whether using has contents
            if (owningNode.TypeSwitch((UsingDirectiveSyntax u) => u.Name == null || u.Name.IsMissing))
            {
                return false;
            }

            // make sure there is no open string literals
            var previousToken = lastToken.GetPreviousToken();
            if (previousToken.Kind() == SyntaxKind.StringLiteralToken && previousToken.ToString().Last() != '"')
            {
                return false;
            }

            if (previousToken.Kind() == SyntaxKind.CharacterLiteralToken && previousToken.ToString().Last() != '\'')
            {
                return false;
            }

            // now, check embedded statement case
            if (owningNode.IsEmbeddedStatementOwner())
            {
                var embeddedStatement = owningNode.GetEmbeddedStatement();
                if (embeddedStatement == null || embeddedStatement.Span.IsEmpty)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// get last token of the given using/field/statement if one exists
        /// </summary>
        private static bool TryGetLastToken(SourceText text, int position, SyntaxNode owningNode, out SyntaxToken lastToken)
        {
            lastToken = owningNode.GetLastToken(includeZeroWidth: true);

            // regardless whether it is missing token or not, if last token is close brace
            // don't do anything
            if (lastToken.Kind() == SyntaxKind.CloseBraceToken)
            {
                return false;
            }

            // last token must be on the same line as the caret
            var line = text.Lines.GetLineFromPosition(position);
            var locatedAtTheEndOfLine = LocatedAtTheEndOfLine(line, lastToken);
            if (!locatedAtTheEndOfLine && text.Lines.IndexOf(lastToken.Span.End) != line.LineNumber)
            {
                return false;
            }

            // if we already have last semicolon, we don't need to do anything
            if (!lastToken.IsMissing && lastToken.Kind() == SyntaxKind.SemicolonToken)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// check whether the line is located at the end of the line
        /// </summary>
        private static bool LocatedAtTheEndOfLine(TextLine line, SyntaxToken lastToken)
        {
            return lastToken.IsMissing && lastToken.Span.End == line.EndIncludingLineBreak;
        }

        /// <summary>
        /// find owning usings/field/statement of the given position
        /// </summary>
        private static SyntaxNode GetOwningNode(SyntaxNode root, int position)
        {
            // make sure caret position is somewhere we can find a token
            var token = root.FindTokenFromEnd(position);
            if (token.Kind() == SyntaxKind.None)
            {
                return null;
            }

            return token.GetAncestors<SyntaxNode>()
                        .Where(n => n is StatementSyntax || n is BaseFieldDeclarationSyntax || n is UsingDirectiveSyntax)
                        .FirstOrDefault();
        }
    }
}
