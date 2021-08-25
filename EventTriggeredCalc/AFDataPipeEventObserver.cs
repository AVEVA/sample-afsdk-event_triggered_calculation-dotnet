using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OSIsoft.AF.Data;

namespace EventTriggeredCalc
{
    public class AFDataPipeEventObserver : IObserver<AFDataPipeEvent>
    {
        void IObserver<AFDataPipeEvent>.OnCompleted()
        {
            // do nothing. This is cleaned up in the main program
        }

        void IObserver<AFDataPipeEvent>.OnError(Exception error)
        {
            Console.WriteLine($"observer error occurred: {error.Message}");
        }

        void IObserver<AFDataPipeEvent>.OnNext(AFDataPipeEvent value)
        {
            Program.ProcessUpdate(value);
        }
    }
}
