using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ScreenBreak
{
    /// <summary>A small clickable window placed on top of a popup.</summary>
    internal interface IPopupButton
    {
        /// <summary>Shows the button on <paramref name="host"/>, the popup's rectangle on one monitor.</summary>
        void ShowIn(Rectangle host);
        void HideButton();
        void SetColors(Color background, Color text);
    }

    /// <summary>One button window per popup rectangle, so every monitor gets its own.</summary>
    internal class ButtonSet<T> where T : Form, IPopupButton
    {
        private readonly Func<T> create;
        private readonly List<T> forms = new List<T>();

        public ButtonSet(Func<T> create)
        {
            this.create = create;
        }

        public void ShowIn(IList<Rectangle> hosts, Color background, Color text)
        {
            while (forms.Count < hosts.Count) forms.Add(create());
            while (forms.Count > hosts.Count)
            {
                forms[forms.Count - 1].Dispose();
                forms.RemoveAt(forms.Count - 1);
            }
            for (int i = 0; i < forms.Count; i++)
            {
                forms[i].SetColors(background, text);
                forms[i].ShowIn(hosts[i]);
            }
        }

        public void Hide()
        {
            foreach (T f in forms) f.HideButton();
        }

        public void BringToTop()
        {
            foreach (T f in forms) if (f.Visible) Native.BringToTop(f.Handle);
        }

        public void Dispose()
        {
            foreach (T f in forms) f.Dispose();
            forms.Clear();
        }
    }
}
