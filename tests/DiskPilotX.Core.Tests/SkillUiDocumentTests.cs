using DiskPilotX.Core.Models;
namespace DiskPilotX.Core.Tests;
public sealed class SkillUiDocumentTests
{
    [Fact] public void AcceptsKnownControlsAndBoundValues() { var ui = SkillUiDocument.Parse("""<StackPanel><TextBlock Text="{Binding summary}"/><TextBox Name="days" Text="30"/><Button Content="预览" Command="preview"/></StackPanel>"""); Assert.Equal(3, ui.Children.Count); }
    [Theory]
    [InlineData("<ObjectDataProvider/>")]
    [InlineData("""<Button Click="DeleteAll"/>""")]
    [InlineData("""<TextBlock Text="{x:Static System.Environment.UserName}" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"/>""")]
    [InlineData("<!DOCTYPE a [<!ENTITY t SYSTEM 'file:///C:/secret'>]><TextBlock>&t;</TextBlock>")]
    [InlineData("""<StackPanel><TextBox Name="same"/><TextBox Name="same"/></StackPanel>""")]
    [InlineData("""<TextBox Name="x" Text="{Binding GetType}"/>""")]
    public void RejectsExecutableOrAmbiguousMarkup(string markup) => Assert.ThrowsAny<Exception>(() => SkillUiDocument.Parse(markup));
}
