using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Android.Graphics;
using Android.Util;
using static Android.Provider.MediaStore.Audio;

namespace RFIDTrackBin
{
    public class HistogramView : View
    {
        private int w;
        private int h;
        private Paint paint = new Paint();
        private float testTextSize = 48f;

        private int[] colors = {
            Color.Rgb(176, 23, 31),
            Color.Rgb(227, 23, 13),
            Color.Rgb(255, 0, 0),
            Color.Rgb(255, 69, 0),
            Color.Rgb(255, 97, 0),
            Color.Rgb(255, 128, 0),
            Color.Rgb(255, 153, 18),
            Color.Rgb(255, 180, 100),
            Color.Rgb(255, 215, 0),
            Color.Rgb(255, 255, 0),
            Color.Rgb(250, 255, 50),
            Color.Rgb(245, 222, 179),
            Color.Rgb(255, 235, 205),
            Color.Rgb(250, 255, 240)
        };

        private HistogramData data = null;

        public HistogramView(Context context) : base(context)
        {
        }

        public HistogramView(Context context, IAttributeSet attrs) : base(context, attrs)
        {
        }

        public HistogramView(Context context, IAttributeSet attrs, int defStyleAttr) : base(context, attrs, defStyleAttr)
        {
        }

        protected override void OnDraw(Canvas canvas) // Just to show the updated OnDraw-method
        {
            //int spacing = this.w / 5;
            //int shift = spacing / 2;
            //int bottomMargin = 10;

            //var paintCircle = new Paint() { Color = Color.Black };
            //for (int i = 0; i < 5; i++)
            //{
            //    int x = i * spacing + shift;
            //    int y = this.h - 20 * 2;
            //    canvas.DrawCircle(x, y, 60, paintCircle);
            //}

            Paint paint = new Paint();
            int lineWidth = 5;

            paint.Color = Color.Black;
            paint.StrokeWidth = 1;
            canvas.DrawLine(10, (float)(h * 0.1), 10, h, paint);
            canvas.DrawLine(10, h, (w), h, paint);
            canvas.DrawLine((w), h, (w), (float)(h * 0.1), paint);
            canvas.DrawLine((w), (float)(h * 0.1), 10, (float)(h * 0.1), paint);

            if (data == null)
            {
                return;
            }

            float y = (float)(h * 0.55);
            float perValueHeight = (float)(h * 0.8) - lineWidth; ;
            float perValueWidth = (float)(w * 0.9) / data.size;
            paint.StrokeWidth = perValueWidth;
            paint.SetStyle(Paint.Style.Stroke);

            float x0, x1 = 0;
            for (int i = 0; i < data.value; i++)
            {
                paint.Color = Color.Rgb(255, 128, 0);
                x0 = 10 + ((float)lineWidth / 2) + (perValueWidth * i);
                x1 = x0 + perValueWidth + lineWidth;
                canvas.DrawLine(x0, y, x1, y, paint);
            }
        }

        protected override void OnSizeChanged(int w, int h, int oldw, int oldh)
        {
            base.OnSizeChanged(w, h, oldw, oldh);
            this.w = w - 10;
            this.h = h - 10;
            Log.Error("TAG", "this.w:" + this.w + ", this.h:" + this.h);
        }

        public void update(HistogramData data)
        {
            this.data = data;
            if (data == null)
            {
                return;
            }
            Invalidate();
        }
    }
}