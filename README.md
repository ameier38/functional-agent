# Functional Agent
Typewriter console application which includes
examples of agents using F#'s MailboxProcessor
to control asynchronous workflows.

The three types of agents used are:
- `RateAgent` -> used to limit the rate at which work is processed.
- `BlockingAgent` -> used to limit amount of work processed in parallel.
- `BufferAgent` -> used to limit the amount of memory that is consumed.

In this example application, each key press first passes
through the `RateAgent` to limit how quickly the typing is
processed. Next the keys presses are send to the `BlockingAgent`
to process the key presses in parallel. A small delay is added
to each key press to show how this can affect the process. Lastly,
the key presses are buffered by the `BufferAgent` up to a certain
line length and then written to a file.

This application tries to mimic real world processes such as
making an API request (the key press) and processing returned
data (writing the keys to a file). When working with a large
amount of data or sending many API requests, these agents can
help the reliability of your application by not overloading
memory or CPU, or overloading another server which does not
have limits in place.

## Usage
```
docker-compose run --rm typewriter
```
