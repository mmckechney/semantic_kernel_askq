﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DocumentQuestions.Console
{
   internal class StartArgs
   {
      public string[] Args { get; set; }
      public StartArgs(string[] args)
      {
         Args = args;
      }
   }
}
