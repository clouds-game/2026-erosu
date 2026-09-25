using ChromaDrop.Headless;

var engine = new JsonEngine();
while (Console.ReadLine() is { } line)
{
  Console.WriteLine(engine.Handle(line));
  Console.Out.Flush();
}
