using System.Windows;
using SelectionTranslator.Core.Interop;

namespace SelectionTranslator.Tests.Interop;

public class ClipboardSnapshotTests
{
    [Fact]
    public void Clone_detaches_text_data()
    {
        var result = StaRunner.Run(() =>
        {
            var source = new DataObject();
            source.SetData(DataFormats.UnicodeText, "hello");

            var copy = ClipboardSnapshot.Clone(source);

            return (
                Copy: copy,
                Text: copy.GetData(DataFormats.UnicodeText) as string,
                SameInstance: ReferenceEquals(source, copy));
        });

        Assert.False(result.SameInstance);
        Assert.Equal("hello", result.Text);
    }
}