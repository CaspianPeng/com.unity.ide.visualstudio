using NUnit.Framework;
using Unity.CodeEditor;

namespace Microsoft.Unity.VisualStudio.Editor.Tests
{
    class SolutionGenerationTestBase
    {
        string m_EditorPath;
        protected SynchronizerBuilder m_Builder;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_EditorPath = CodeEditor.CurrentEditorInstallation;
            CodeEditor.SetExternalScriptEditor("NotSet");
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            CodeEditor.SetExternalScriptEditor(m_EditorPath);
        }

        [SetUp]
        public void SetUp()
        {
            m_Builder = new SynchronizerBuilder();
        }
    }
}
