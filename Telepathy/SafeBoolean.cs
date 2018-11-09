// a very simple locked 'bool'
// (we can't do lock(int) so we need an object and since we also need a max
//  check, we might as well put it into a class here)
namespace Telepathy
{
  public class SafeBoolean
  {
    bool _State;
    public bool State
    {
      get
      {
        lock (this) { return _State; }
      }
      set
      {
        lock (this) { _State = value; }
      }
    }
  }
}