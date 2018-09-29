#load "MailboxProcessorExtensions.fs"
#load "EventStore.fs"
open EventStore
let inMemoryEventStore = 
    createInMemoryEventStore<string,string> 
        (fun ({Expected=StreamVersion e; Actual = StreamVersion a }) ->
            sprintf "Expected v%i but encountered v%i" e a)
inMemoryEventStore.AddSubscriber (SubscriptionToken "FirstSubscriber") (printfn "[FirstSubscriber] Handling: %A")  

let streamId = StreamId(System.Guid.NewGuid())
[
    inMemoryEventStore.SaveEvents streamId (StreamVersion 0) ["Hello";"World";"POLICE"]  
    inMemoryEventStore.SaveEvents streamId (StreamVersion 3) ["Hello2";"World2"]  
    inMemoryEventStore.SaveEvents streamId (StreamVersion 5) ["AXE"]
    inMemoryEventStore.SaveEvents streamId (StreamVersion 6) ["GREETINGS"]
    inMemoryEventStore.SaveEvents streamId (StreamVersion 8) ["OOPS!"]
] |> List.mapi (fun i v -> printfn "%i: %A" i v)

inMemoryEventStore.SaveEvents streamId (StreamVersion 7) ["Let's try this again"]
inMemoryEventStore.SaveEvents streamId (StreamVersion 8) ["Much better!"]

inMemoryEventStore.GetEvents streamId
inMemoryEventStore.GetEvents (StreamId (System.Guid.NewGuid()))
inMemoryEventStore.RemoveSubscriber (SubscriptionToken "FirstSubscriber")
inMemoryEventStore.SaveEvents streamId (StreamVersion 9) ["AND";"MY";"AXE"]