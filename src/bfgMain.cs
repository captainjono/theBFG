using Rxns;

namespace theBFG
{
    class bfgMain
    {
        static void Main(string[] args)
        {
            theBfg.Reload(args: args).Until();
        }
    }
}