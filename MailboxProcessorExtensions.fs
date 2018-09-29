[<AutoOpen>]
module MailboxProcessorExtensions

type MailboxProcessor<'T> with
    static member StartSupervised (errorHandler: exn -> unit, body : MailboxProcessor<_> -> Async<unit>) =
        let watchdog f x = async {
            while true do
                try do! f x
                with exn -> 
                    errorHandler exn
        }
        MailboxProcessor.Start (fun inbox -> watchdog body inbox)