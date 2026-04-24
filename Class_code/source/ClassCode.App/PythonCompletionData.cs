using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using System;

namespace ClassCode.App
{
    // 자동 완성 목록에 표시될 데이터 구조
    public class PythonCompletionData : ICompletionData
    {
        public PythonCompletionData(string text)
        {
            this.Text = text;
        }

        public System.Windows.Media.ImageSource Image => null; // 아이콘을 넣고 싶을 때 사용

        public string Text { get; private set; }

        // 목록에서 선택했을 때 보여줄 설명창 내용
        public object Content => this.Text;
        public object Description => "Python 예약어: " + this.Text;

        public double Priority => 0;

        // 사용자가 선택했을 때 에디터에 실제로 입력되는 로직
        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionEventArgs)
        {
            // 1. 현재 커서 위치를 가져옵니다.
            int endOffset = textArea.Caret.Offset;

            // 2. 현재 입력 중인 단어의 시작 지점을 찾습니다. 
            // 커서 앞쪽으로 가면서 문자나 숫자인 부분까지를 단어 영역으로 봅니다.
            int startOffset = endOffset;
            while (startOffset > 0 && char.IsLetterOrDigit(textArea.Document.GetCharAt(startOffset - 1)))
            {
                startOffset--;
            }

            // 3. 찾은 영역(startOffset부터 현재까지)을 선택한 예약어(this.Text)로 교체합니다.
            // 이렇게 하면 'p'를 쳤을 때 'p' 영역이 통째로 'print'로 바뀝니다.
            textArea.Document.Replace(startOffset, endOffset - startOffset, this.Text);
        }
    }
}