#load "MailboxProcessorExtensions.fs"
open System
let logException (exn: Exception) =
    printfn "Agent has stopped due to an exception: %s(%s)" (exn.GetType().Name) (exn.Message)
type Msg = 
    | Say of string 
    | Explode
let agent = MailboxProcessor.StartSupervised(logException, fun (inbox: MailboxProcessor<Msg>) ->  
    let rec loop() =
        async {
            let! msg = inbox.Receive()
            match msg with
            | Explode -> 
                raise (new ArgumentException("OH DEAR, WE HAD AN ARGUMENT AND THEN THERE WAS A TERRIBLE EXPLOSION!"))
            | Say words -> 
                printfn "	    _______                "
                printfn "     _/       \_              "
                printfn "    / |       | \             "
                printfn "   /  |__   __|  \            "
                printfn "  |__/((o| |o))\__|           "
                printfn "  |      | |      |           "
                printfn "  |\     |_|     /|           "
                printfn "  | \           / |           "
                printfn "   \| / _____ \ |/            "
                printfn "    \ | \___/ | / ________ %s " words
                printfn "     \_________/              "
                printfn "      _|_____|_               "
                printfn " ____|_________|____          "
                printfn "/                   \         "
                return! loop()
        }
    printfn "Starting agent"
    loop() 
)

agent.Post(Say "hello")
agent.Post(Explode)
agent.Post(Say "My name is C-3P1.  I am the successor to C-3P0.")
agent.Post(Say "I do believe that you and I have much business to attend to!")
agent.Start()