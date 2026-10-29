using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DiskPilotX.Core.Models;
using Path=System.Windows.Shapes.Path;

namespace DiskPilotX.App.Controls;

public sealed partial class SkillChartControl
{
    private static readonly DependencyProperty RevealProgressProperty=DependencyProperty.Register(
        "RevealProgress",typeof(double),typeof(SkillChartControl),new PropertyMetadata(1d,OnRevealProgressChanged));
    private readonly List<Action<double>> revealUpdates=[];
    private readonly List<Action> resetHover=[];
    private AnimationClock? revealClock;
    private SkillUiSection? revealedSection;
    private bool watchingSystem;
    private double RevealProgress=>(double)GetValue(RevealProgressProperty);
    internal object MotionState=>new {progress=RevealProgress,animationEnabled=SystemParameters.ClientAreaAnimation,
        clockId=revealClock is null?0:System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(revealClock),clockState=revealClock?.CurrentState.ToString()};
    private static void OnRevealProgressChanged(DependencyObject target,DependencyPropertyChangedEventArgs args)
    {
        var control=(SkillChartControl)target;
        foreach(var update in control.revealUpdates)update((double)args.NewValue);
    }
    private static DoubleAnimation? CreateMotion(double from,double to,int milliseconds,bool enabled)=>enabled
        ?new DoubleAnimation(from,to,TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction=new CubicEase { EasingMode=EasingMode.EaseOut },FillBehavior=FillBehavior.Stop }
        :null;
    private void OnMotionLoaded()
    {
        if(!watchingSystem){SystemParameters.StaticPropertyChanged+=OnSystemMotionChanged;watchingSystem=true;}
        StartReveal();
    }
    private void StartReveal()
    {
        if(ReferenceEquals(revealedSection,Section))return;
        var from=revealClock?.CurrentState==ClockState.Active?RevealProgress:0;
        revealedSection=Section;
        var previous=revealClock;revealClock=null;previous?.Controller?.Remove();
        BeginAnimation(RevealProgressProperty,null);SetValue(RevealProgressProperty,1d);
        if(Section is not {IsChart:true} || !Section.Items.Any(item=>double.IsFinite(item.Metric)&&item.Metric>0))return;
        var animation=CreateMotion(from,1,400,SystemParameters.ClientAreaAnimation);
        if(animation is null)return;
        // 基值先落到起点；不使用SeekAlignedToLastTick，避免空闲后的旧tick让新动画瞬间结束。
        SetValue(RevealProgressProperty,from);
        animation.FillBehavior=FillBehavior.HoldEnd;
        var clock=(AnimationClock)animation.CreateClock(true);
        revealClock=clock;
        ApplyAnimationClock(RevealProgressProperty,clock,HandoffBehavior.SnapshotAndReplace);
        clock.Controller!.Begin();
    }
    private void ResetRenderedMotion()
    {
        foreach(var reset in resetHover)reset();
        resetHover.Clear();revealUpdates.Clear();
    }
    private void OnMotionUnloaded()
    {
        if(watchingSystem){SystemParameters.StaticPropertyChanged-=OnSystemMotionChanged;watchingSystem=false;}
        FinishMotion();
    }
    private void FinishMotion()
    {
        var previous=revealClock;revealClock=null;previous?.Controller?.Remove();
        BeginAnimation(RevealProgressProperty,null);SetValue(RevealProgressProperty,1d);
        foreach(var reset in resetHover)reset();
    }
    private void OnSystemMotionChanged(object? sender,PropertyChangedEventArgs args)
    {
        if(args.PropertyName is not (null or nameof(SystemParameters.ClientAreaAnimation)))return;
        if(!SystemParameters.ClientAreaAnimation)
        {
            if(Dispatcher.CheckAccess())FinishMotion();else Dispatcher.BeginInvoke(new Action(FinishMotion));
        }
    }
    private void RevealBar(Border fill)
    {
        var scale=new ScaleTransform(1,1);fill.RenderTransform=scale;
        void Update(double progress)=>scale.ScaleX=progress;
        revealUpdates.Add(Update);Update(RevealProgress);
    }
    private void RevealSlice(Path shape,Path hit,double start,double end,bool donut)
    {
        var final=hit.Data;
        void Update(double progress)
        {
            shape.Data=progress>=end?final:progress<=start?Geometry.Empty:Slice(start,Math.Min(end,progress),donut);
            hit.IsHitTestVisible=progress>=1;
        }
        revealUpdates.Add(Update);Update(RevealProgress);
    }
    private void AttachHover(FrameworkElement target,FrameworkElement? legend,SolidColorBrush brush,TranslateTransform? offset=null,double x=0,double y=0)
    {
        var normal=brush.Color;
        var highlighted=Color.FromArgb(normal.A,(byte)(normal.R+(255-normal.R)*.16),(byte)(normal.G+(255-normal.G)*.16),(byte)(normal.B+(255-normal.B)*.16));
        void Move(DependencyProperty property,double value)
        {
            if(offset is null)return;
            var current=(double)offset.GetValue(property);offset.SetValue(property,value);
            offset.BeginAnimation(property,CreateMotion(current,value,150,SystemParameters.ClientAreaAnimation),HandoffBehavior.SnapshotAndReplace);
        }
        void Hover(bool active)
        {
            var color=active?highlighted:normal;var current=brush.Color;brush.Color=color;
            brush.BeginAnimation(SolidColorBrush.ColorProperty,SystemParameters.ClientAreaAnimation
                ?new ColorAnimation(current,color,TimeSpan.FromMilliseconds(150)) {EasingFunction=new CubicEase {EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop}:null,HandoffBehavior.SnapshotAndReplace);
            Move(TranslateTransform.XProperty,active?x:0);Move(TranslateTransform.YProperty,active?y:0);
        }
        void Reset()
        {
            brush.BeginAnimation(SolidColorBrush.ColorProperty,null);brush.Color=normal;
            if(offset is not null){offset.BeginAnimation(TranslateTransform.XProperty,null);offset.BeginAnimation(TranslateTransform.YProperty,null);offset.X=0;offset.Y=0;}
        }
        target.MouseEnter+=(_,_)=>Hover(true);target.MouseLeave+=(_,_)=>Hover(false);
        if(legend is not null){legend.MouseEnter+=(_,_)=>Hover(true);legend.MouseLeave+=(_,_)=>Hover(false);}
        resetHover.Add(Reset);
    }
}
