' Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

Imports Microsoft.CodeAnalysis.Editor.VisualBasic.EndConstructGeneration
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic
Imports Microsoft.CodeAnalysis.VisualBasic.Symbols
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax
Imports Microsoft.VisualStudio.Text
Imports Roslyn.Test.EditorUtilities
Imports Roslyn.Test.Utilities

Namespace Microsoft.CodeAnalysis.Editor.VisualBasic.UnitTests.EndConstructGeneration
    Public Class WhiteSpacesInsertTests
        <Fact, Trait(Traits.Feature, Traits.Features.EndConstructGeneration)>
        Public Sub VerifyInsertWhiteSpace()
            VerifyStatementEndConstructApplied(
                before:={"Class X",
                         "  Sub y()",
                         "End Class"},
                beforeCaret:={1, -1},
                 after:={"Class X",
                         "  Sub y()",
                         "",
                         "  End Sub",
                         "End Class"},
                afterCaret:={2, -1})
        End Sub

        <Fact, Trait(Traits.Feature, Traits.Features.EndConstructGeneration)>
        Public Sub VerifyInsertTabSpace()
            VerifyStatementEndConstructApplied(
                before:={"Class X",
                         vbTab + vbTab + "Sub y()",
                         "End Class"},
                beforeCaret:={1, -1},
                 after:={"Class X",
                         vbTab + vbTab + "Sub y()",
                         "",
                         vbTab + vbTab + "End Sub",
                         "End Class"},
                afterCaret:={2, -1})
        End Sub

        <Fact, Trait(Traits.Feature, Traits.Features.EndConstructGeneration)>
        Public Sub VerifyInsertDoubleWideWhiteSpace()
            VerifyStatementEndConstructApplied(
                before:={"Class X",
                         " Sub y()",
                         "End Class"},
                beforeCaret:={1, -1},
                 after:={"Class X",
                         " Sub y()",
                         "",
                         " End Sub",
                         "End Class"},
                afterCaret:={2, -1})

        End Sub
    End Class
End Namespace
